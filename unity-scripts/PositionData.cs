using System;

/// <summary>
/// Cuerpo JSON que intercambia el servicio de referencia de la Parte 1.
///
/// Los nombres de los campos deben ser EXACTAMENTE posX, posY y posZ: el
/// servicio responde 422 tanto si falta alguno como si llega uno desconocido.
/// Por eso no se puede enviar directamente un Vector3, que serializa como
/// {"x","y","z"}.
///
/// Se usa JsonUtility y no interpolacion de cadenas porque JsonUtility escribe
/// siempre el separador decimal con punto. Construir el JSON a mano en un
/// Windows configurado en espanol produciria "1,5" y el servicio respondria
/// 400 Invalid JSON.
/// </summary>
[Serializable]
public class PositionData
{
    public float posX;
    public float posY;
    public float posZ;
}
