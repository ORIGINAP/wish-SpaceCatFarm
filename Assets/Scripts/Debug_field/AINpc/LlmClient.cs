using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LlmClient
{
    private const string Endpoint  = "https://factchat-cloud.mindlogic.ai/v1/gateway/chat/completions/";
    private const string UserAgent = "WISH-Game/1.0";

    public string Model       = "claude-haiku-4-5-20251001";
    public int    MaxTokens   = 160;
    public float  Temperature = 0.4f;
    public int    TimeoutSec  = 20;

    public bool Busy { get; private set; }

    public int Calls            { get; private set; }
    public int PromptTokens     { get; private set; }
    public int CompletionTokens { get; private set; }

    private string fileKey;
    private bool   keyLoaded;

    [Serializable] private class Msg      { public string role; public string content; }
    [Serializable] private class Request  { public string model; public Msg[] messages; public int max_tokens; public float temperature; }
    [Serializable] private class Choice   { public Msg message; }
    [Serializable] private class Usage    { public int prompt_tokens; public int completion_tokens; }
    [Serializable] private class Response { public Choice[] choices; public Usage usage; }

    public bool HasKey => !string.IsNullOrEmpty(ApiKey);

    private string ApiKey
    {
        get
        {
            string userKey = GameSettings.AiApiKey;
            if (!string.IsNullOrEmpty(userKey)) return userKey;
            if (!keyLoaded) { fileKey = LoadKey(); keyLoaded = true; }
            return fileKey;
        }
    }

    public IEnumerator Send(List<(string role, string content)> messages, Action<string> onDone, Action<string> onError)
    {
        string key = ApiKey;
        if (string.IsNullOrEmpty(key)) { onError?.Invoke("API 키가 없어요 (옵션 → AI 조수에서 넣어 주세요)"); yield break; }
        if (Busy) { onError?.Invoke("아직 이전 말을 생각하는 중이에요"); yield break; }

        var req = new Request
        {
            model       = Model,
            max_tokens  = MaxTokens,
            temperature = Temperature,
            messages    = new Msg[messages.Count]
        };
        for (int i = 0; i < messages.Count; i++)
            req.messages[i] = new Msg { role = messages[i].role, content = messages[i].content };

        Busy = true;
        using var www = new UnityWebRequest(Endpoint, "POST")
        {
            uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(req))),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout         = TimeoutSec
        };
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", "Bearer " + key);
        www.SetRequestHeader("User-Agent", UserAgent);

        yield return www.SendWebRequest();
        Busy = false;

        if (www.result != UnityWebRequest.Result.Success)
        {
            string reason = www.responseCode switch
            {
                401 or 403 => "API 키가 거부됐어요",
                429        => "요청이 너무 많아요. 잠시 후 다시 말해 주세요",
                0          => "서버에 연결하지 못했어요",
                _          => $"통신 오류 ({www.responseCode})"
            };
            Debug.LogWarning($"[AINpc] LLM 요청 실패: {www.responseCode} {www.error}");
            onError?.Invoke(reason);
            yield break;
        }

        Response res = null;
        try { res = JsonUtility.FromJson<Response>(www.downloadHandler.text); }
        catch (Exception e) { Debug.LogWarning($"[AINpc] 응답 해석 실패: {e.Message}"); }

        string content = res?.choices != null && res.choices.Length > 0 ? res.choices[0].message?.content : null;
        if (string.IsNullOrEmpty(content)) { onError?.Invoke("대답을 이해하지 못했어요"); yield break; }

        Calls++;
        if (res.usage != null)
        {
            PromptTokens     += res.usage.prompt_tokens;
            CompletionTokens += res.usage.completion_tokens;
            Debug.Log($"[AINpc] LLM 호출 #{Calls}: 입력 {res.usage.prompt_tokens} / 출력 {res.usage.completion_tokens} 토큰 " +
                      $"(누계 {PromptTokens}/{CompletionTokens})");
        }
        onDone?.Invoke(content);
    }

    private static string LoadKey()
    {
        string env = Environment.GetEnvironmentVariable("WISH_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

        var paths = new List<string>();
#if UNITY_EDITOR
        paths.Add(Path.Combine(Application.dataPath, "Scripts", "Debug_field", "env"));
#endif
        paths.Add(Path.Combine(Application.dataPath, "..", "env"));
        paths.Add(Path.Combine(Application.persistentDataPath, "env"));

        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (t.StartsWith("API_KEY=")) return t.Substring("API_KEY=".Length).Trim();
                }
            }
            catch (Exception e) { Debug.LogWarning($"[AINpc] env 파일을 읽지 못했습니다: {e.Message}"); }
        }
        return null;
    }
}
