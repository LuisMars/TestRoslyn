namespace TestRoslyn.Dummy;
public interface IEventBus
{
    void Publish<T>(T message) where T : IEvent;
    void Subscribe<T>(Action<T> action);
}

public class EventBus : IEventBus
{
    public void Publish<T>(T message) where T : IEvent
    {
    }

    public void Subscribe<T>(Action<T> action)
    {
        throw new NotImplementedException();
    }
}