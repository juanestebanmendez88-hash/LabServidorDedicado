# Laboratorio: Servidor Dedicado

Servicios Multimedia — Facultad de Ingeniería, Universidad San Buenaventura, Bogotá.

Sincronización entre dos instancias de Unity por **sondeo periódico (polling)**, comparando dos modos:

- **Parte 1 — estado:** cada cliente publica y consulta posiciones. El servicio guarda solo el último valor, así que las posiciones intermedias se pierden.
- **Parte 2 — eventos:** cada cliente publica acciones. El servicio las guarda todas en orden y cada cliente pide solo las que aún no ha recibido, así que no se pierde ninguna.

## Contenido del repositorio

| Carpeta | Qué es |
|---|---|
| `event-service/` | Servicio de eventos de la Parte 2 (Flask + Docker) |
| `unity-client/` | Proyecto Unity con los clientes de las partes 1 y 2 |
| `resultados/` | CSV de los experimentos A, B y C |

El servicio de referencia de la Parte 1 **no está aquí**: es el del profesor, se clona aparte (ver abajo).

## Requisitos

- **Docker Desktop.** En Windows necesita WSL 2: si falta, ejecutar `wsl --install` en PowerShell como administrador y reiniciar.
- **Unity 6000.x** para el proyecto cliente.

## Cómo ejecutar

### 1. Servicio de referencia — Parte 1 (puerto 5005)

```bash
git clone https://github.com/memin2522/DedicatedServer-Api
cd DedicatedServer-Api
docker compose up --build -d
```

Queda en `http://localhost:5005`, con documentación interactiva en `http://localhost:5005/swagger-ui`.

| Método | Ruta | Cuerpo | Respuestas |
|---|---|---|---|
| POST | `/server/{game_id}/{player_id}` | `{"posX":f,"posY":f,"posZ":f}` | 201; 422 si falta un campo |
| GET | `/server/{game_id}/{player_id}` | — | 200 con la posición; 404 si no existe |

### 2. Servicio de eventos — Parte 2 (puerto 5006)

```bash
cd event-service
docker compose up --build -d
```

Queda en `http://localhost:5006`, con documentación en `http://localhost:5006/swagger-ui`.

| Método | Ruta | Cuerpo | Respuestas |
|---|---|---|---|
| POST | `/games/{game_id}/events` | `{"player_id":"p1","type":"LeverToggled","payload":{...},"event_id":"<opcional>"}` | 201 con `seq` y `timestamp`; 200 si `event_id` ya existía; 422 si faltan datos |
| GET | `/games/{game_id}/events?since={seq}` | — | 200 con `events` (los de `seq` mayor que `since`, en orden) y `last_seq` |

Ejemplo:

```bash
curl -X POST http://localhost:5006/games/g1/events \
  -H "Content-Type: application/json" \
  -d '{"player_id":"p1","type":"LeverToggled","payload":{"x":1,"y":0,"z":2}}'
# -> {"seq":1,"timestamp":"2026-10-07T03:11:56.812776+00:00"}

curl "http://localhost:5006/games/g1/events?since=0"
# -> {"events":[{"seq":1,"player_id":"p1","type":"LeverToggled",...}],"last_seq":1}
```

Los dos servicios pueden correr al mismo tiempo, porque usan puertos distintos.

Para detenerlos: `docker compose down` en cada carpeta.

### 3. Cliente Unity

1. Abrir `unity-client/` con Unity 6000.x.
2. Activar **Allow downloads over HTTP** en *Player Settings → Other Settings*, porque Unity bloquea HTTP sin cifrar por defecto.
3. Configurar en el Inspector: URL de los servicios, `game_id`, `player_id` local y remoto, y Δt.
4. Para las dos instancias: compilar un build con `player_id = p1` / remoto `p2`, y dejar el editor con `p2` / remoto `p1`.

## Decisiones de diseño del servicio de eventos

- **`seq` empieza en 1**, así `since=0` devuelve todos los eventos de la partida y el cliente arranca con `lastSeq = 0`.
- **`seq` es independiente por partida**, para que dos partidas simultáneas no compartan numeración.
- **Una partida sin eventos responde 200** con lista vacía y `last_seq: 0`, no 404: que todavía no haya eventos no es un error.
- **Un candado (`threading.Lock`) al asignar el `seq`.** El servidor atiende varias peticiones a la vez, así que sin él dos POST simultáneos podrían recibir el mismo número. Verificado con 100 POST concurrentes.
- **`event_id` opcional para idempotencia.** Si un cliente reenvía un evento porque no recibió la respuesta a tiempo, el servidor reconoce el id y devuelve el `seq` original con código 200, en vez de registrarlo dos veces. Es la respuesta a la pregunta de análisis 3.
- **Almacenamiento en memoria.** Es suficiente para el laboratorio y mantiene el servicio comparable con el de referencia. Sus límites (se pierde todo al reiniciar, y la memoria crece sin cota) se discuten en la pregunta de análisis 4.

## Pruebas

```bash
cd event-service
pip install -r requirements.txt pytest
python -m pytest -q
```

17 pruebas que cubren el contrato: `seq` consecutivo e independiente por partida, filtro `since`, validación de datos faltantes, idempotencia por `event_id`, 100 POST concurrentes y una simulación de sondeo incremental donde el cliente recibe los 20 eventos exactamente una vez.
