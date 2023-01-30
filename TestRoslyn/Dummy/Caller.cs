namespace TestRoslyn.Dummy;
public class Caller
{
    IEventBus eventBus = new EventBus();
    public Caller()
    {
    }

    public void CallParent()
    {
        Call();
    }

    public void Call()
    {
        eventBus.Publish(new DummyEvent());
    }
    public void Call2()
    {
        CallParent();
        eventBus.Publish(new TestEvent());
    }
}
