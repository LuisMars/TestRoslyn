namespace TestRoslyn.Dummy;
public class Startup
{
    EventBus eventBus = new EventBus();
    Subscriber subscriber = new Subscriber();
    SubscriberTest subscriberTest = new SubscriberTest();
    public Startup()
    {

    }

    public void Init()
    {
        InitEvents();
    }

    private void InitEvents()
    {
        eventBus.Subscribe<Test2Event>(m => subscriber.Execute(m));
        eventBus.Subscribe<TestEvent>(subscriberTest.Execute);
    }
}
