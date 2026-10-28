using NUnit.Framework;
using UnityEngine;

public class ServicesTests
{
    private sealed class FakeService { }

    [TearDown]
    public void TearDown() => Services.ResetStatics();

    [Test]
    public void Register_ThenGet_ReturnsSameObject()
    {
        var service = new FakeService();

        Assert.IsTrue(Services.Register(service));
        Assert.AreSame(service, Services.Get<FakeService>());
    }

    [Test]
    public void Register_WhileAnotherIsAlive_IsRefused()
    {
        var first = new FakeService();
        Services.Register(first);

        Assert.IsFalse(Services.Register(new FakeService()));
        Assert.AreSame(first, Services.Get<FakeService>());
        Assert.IsTrue(Services.Register(first));
    }

    [Test]
    public void Unregister_OnlyRemovesTheRegisteredObject()
    {
        var registered = new FakeService();
        Services.Register(registered);

        Services.Unregister(new FakeService());
        Assert.AreSame(registered, Services.Get<FakeService>());

        Services.Unregister(registered);
        Assert.IsNull(Services.Get<FakeService>());
    }

    [Test]
    public void Register_AfterUnityObjectWasDestroyed_ReplacesIt()
    {
        var destroyed = new Texture2D(1, 1);
        Services.Register(destroyed);
        Object.DestroyImmediate(destroyed);

        var fresh = new Texture2D(1, 1);
        Assert.IsTrue(Services.Register(fresh));
        Assert.AreSame(fresh, Services.Get<Texture2D>());
        Object.DestroyImmediate(fresh);
    }

    [Test]
    public void ResetStatics_ClearsEverySlot()
    {
        Services.Register(new FakeService());
        Services.Replace("world");

        Services.ResetStatics();

        Assert.IsNull(Services.Get<FakeService>());
        Assert.IsNull(Services.Get<string>());
        Assert.AreEqual(0, Services.Count);
    }
}
