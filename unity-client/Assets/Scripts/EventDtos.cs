using System;

/// <summary>
/// Datos adicionales de un evento. JsonUtility no sabe deserializar un
/// diccionario libre, asi que el payload se fija a estos campos. El servicio
/// acepta cualquier diccionario, de modo que los que no se usen viajan en cero
/// y no estorban.
/// </summary>
[Serializable]
public class EventPayload
{
    public float x, y, z;       // donde ocurrio
    public float dx, dy, dz;    // direccion, para el proyectil
    public int n;               // indice, lo usa el experimento C

    /// <summary>
    /// Jugador sobre el que recae el evento, que no tiene por que ser quien lo
    /// envia: en un PlayerHit quien avisa es el que disparo, pero el que
    /// parpadea es el alcanzado.
    /// </summary>
    public string target;
}

/// <summary>Cuerpo del POST a /games/{game_id}/events.</summary>
[Serializable]
public class EventPost
{
    public string player_id;
    public string type;
    public EventPayload payload;

    /// <summary>
    /// Identificador unico generado por el cliente. Si una respuesta se pierde
    /// y el evento se reenvia, el servicio reconoce este valor y devuelve el
    /// seq original en vez de registrarlo dos veces.
    /// </summary>
    public string event_id;
}

/// <summary>Respuesta del POST: el numero de orden asignado.</summary>
[Serializable]
public class EventAck
{
    public int seq;
    public string timestamp;
}

/// <summary>Un evento tal como lo devuelve el servicio.</summary>
[Serializable]
public class GameEvent
{
    public int seq;
    public string player_id;
    public string type;
    public EventPayload payload;
    public string timestamp;
}

/// <summary>Respuesta del GET a /games/{game_id}/events?since=N.</summary>
[Serializable]
public class EventsResponse
{
    public GameEvent[] events;
    public int last_seq;
}
