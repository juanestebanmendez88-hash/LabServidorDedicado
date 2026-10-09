using System;

[Serializable]
public class EventPayload
{
    public float x, y, z;
    public float dx, dy, dz;
    public int n;

    public string target;
}

[Serializable]
public class EventPost
{
    public string player_id;
    public string type;
    public EventPayload payload;

    public string event_id;
}

[Serializable]
public class EventAck
{
    public int seq;
    public string timestamp;
}

[Serializable]
public class GameEvent
{
    public int seq;
    public string player_id;
    public string type;
    public EventPayload payload;
    public string timestamp;
}

[Serializable]
public class EventsResponse
{
    public GameEvent[] events;
    public int last_seq;
}
