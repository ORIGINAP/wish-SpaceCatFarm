using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Player : MonoBehaviour
{
    public float moveSpeed = 5f;
    private Rigidbody2D rb;
    private Vector2 lastVector = new Vector2(0, -1);
    private Vector2 moveInput;
    public LayerMask detectionMask;
    [Header("IDLE Settings")]
    [SerializeField] private Sprite[] idleSprites;
    private SpriteRenderer spriteRenderer;

    [Header("Raycast Settings")]
    [SerializeField] private float interactionDistance = 2f;

    [Header("설치물 충돌 (발밑 상자)")]
    [Tooltip("발밑 충돌 상자 크기 (월드). 밭(플랜트)을 뺀 설치물이 차지한 칸과 이 상자가 겹치는 쪽으로는 못 갑니다. " +
             "발밑만 재므로 키 큰 설치물도 설치된 칸만 막고, 위로 솟은 부분은 뒤로 지나갈 수 있습니다.")]
    [SerializeField] private Vector2 feetSize   = new(0.5f, 0.2f);
    [Tooltip("발밑 충돌 상자 가운데의 위치")]
    [SerializeField] private Vector2 feetOffset = new(0f, -0.33f);

    public Rect FeetRect => new((Vector2)transform.position + feetOffset - feetSize * 0.5f, feetSize);

    public Vector2 FacingDirection =>
        Mathf.Abs(lastVector.y) >= Mathf.Abs(lastVector.x)
            ? new Vector2(0f, lastVector.y >= 0f ? 1f : -1f)
            : new Vector2(lastVector.x > 0f ? 1f : -1f, 0f);

    private bool isColliding = false;

    [Header("Interaction Cooldown")]
    private float lastInteractionTime;
    [SerializeField] private float cooldownTime = 0.2f;

    public static Player Local => Services.Get<Player>();

    void Awake() => Services.Register(this);

    void OnDestroy() => Services.Unregister(this);

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (rb == null)
        {
            Debug.LogError("불러오기 실패");
        }

        if (GetComponent<BehindTileSorter>() == null) gameObject.AddComponent<BehindTileSorter>();

        if (!TryGetComponent(out CatSkinRenderer skin)) skin = gameObject.AddComponent<CatSkinRenderer>();
        skin.SetLook(PlayerCharacter.Current);
    }

    void Update()
    {
        if(GameStateManager.IsUIMode) return;
    }
    
    void FixedUpdate()
    {
        if(GameStateManager.IsUIMode)
        {
            rb.linearVelocity = Vector2.zero;
            moveInput = Vector2.zero;
            return;
        }

        isColliding = false;
        ApplyMovement();
    }

    public Vector2 HandleMovement()
    {
        Vector2 movement = InputManager.Instance != null
            ? InputManager.Instance.MoveVector
            : TextInputFocus.Active ? Vector2.zero : new Vector2(GameInput.GetAxisRaw("Horizontal"), GameInput.GetAxisRaw("Vertical"));

        if (movement.magnitude > 0)
        {
            lastVector = movement;
        }

        if (movement.magnitude > 1)
        {
            movement.Normalize();
        }

        moveInput = movement;
        return movement;
    }

    private void ApplyMovement()
    {
        if (moveInput == Vector2.zero) return;

        Vector2 moveDir;
        Vector2 calculatedNextPosition = rb.position + (moveInput * moveSpeed * Time.fixedDeltaTime);

        if (GridManager.Instance != null)
            calculatedNextPosition = GridManager.Instance.ClampToWorldBounds(calculatedNextPosition);

        // 발밑 상자로만 칸 충돌
        calculatedNextPosition = TileCollision.Move(rb.position, calculatedNextPosition, feetOffset, feetSize * 0.5f);

        if (isColliding == false)
        {
            moveDir = calculatedNextPosition;
            
            rb.MovePosition(moveDir);
        }
    }
    
    private Vector2 GetFacingDirection()
    {
        if (rb.linearVelocity.magnitude > 0.1f)
        {
            return rb.linearVelocity.normalized;
        }
        return lastVector.normalized;
    }

}