# Montaje del cliente Unity

Cómo está armado el proyecto y cómo se reproducen los experimentos. La escena ya viene
montada en `Assets/Scenes/EscenaArena.unity`; esto documenta qué hay dentro y por qué.

## Scripts

| Archivo | Qué hace |
|---|---|
Están en `Assets/Scripts/`, repartidos por funcionalidad.

| Archivo | Qué hace |
|---|---|
| `Networking/PositionData.cs` | Cuerpo JSON del servicio de referencia (`posX`, `posY`, `posZ`) |
| `Networking/PositionSyncClient.cs` | **Parte 1.** Publica la posición propia y consulta la remota, cada una en su corrutina |
| `Networking/EventDtos.cs` | Cuerpos JSON del servicio de eventos |
| `Networking/EventClient.cs` | **Parte 2.** Envía eventos por una cola y consulta los nuevos con `since` |
| `Gameplay/PlayerController.cs` | Movimiento del jugador local con WASD, relativo a la cámara |
| `Gameplay/ArenaEvents.cs` | **Parte 2.** Traduce teclas en eventos y eventos en reacciones visibles |
| `Experiments/ExperimentRunner.cs` | Experimentos A, B y C, con salida a CSV |
| `Ui/Hud.cs` | Escalado de fuente, estilos y fondo de los textos en pantalla |

## Escena

| Objeto | Qué es | Ajustes |
|---|---|---|
| Piso | `3D Object → Plane` | — |
| `LocalPlayer` | Cápsula del **jugador 1** | Y = 1, material rojo, lleva `PlayerController` |
| `RemotePlayer` | Cápsula del **jugador 2** | Y = 1, material azul, lleva `PlayerController` |
| `NetworkManager` | Objeto vacío | lleva `PositionSyncClient`, `EventClient`, `ArenaEvents` y `ExperimentRunner` |
| Cámara | — | colocada en ángulo, como un juego de pelea |

Los nombres `LocalPlayer` y `RemotePlayer` son históricos: **las dos cápsulas se declaran
por número de jugador**, no por papel. En el Inspector de `PositionSyncClient` se arrastran
a los campos `Jugador 1` y `Jugador 2`, y es el script el que decide cuál controla cada
ventana. Hacerlo al revés —declararlas como «local» y «remota»— fue un error de la primera
versión: esos papeles son relativos a cada instancia, así que la misma cápsula se llamaba
`player1` en una ventana y `player2` en la otra.

Los dos objetos llevan `PlayerController`, pero el script lo **desactiva** en la cápsula que
no corresponde a esta ventana, de modo que cada instancia solo mueve la suya.

## Ajustes del proyecto

| Ajuste | Dónde | Valor | Por qué |
|---|---|---|---|
| Allow downloads over HTTP | *Player → Other Settings* | **Always allowed** | Unity bloquea HTTP sin cifrar y ningún servicio respondería |
| Run In Background | *Player → Resolution and Presentation* | **activado** | si no, la ventana sin foco deja de sondear y se congela la otra |

No hace falta tocar *Active Input Handling*: los scripts usan el Input System nuevo, que es
el que trae la plantilla Universal 3D.

## Inspector de `NetworkManager`

En **Position Sync Client**:

| Campo | Valor |
|---|---|
| Server Url | `http://localhost:5005` |
| Game Id | `g1` |
| Local Player Id | `p1` |
| Remote Player Id | `p2` |
| Delta Time Ms | `200` (se cambia para cada experimento) |
| Jugador 1 / Jugador 2 | arrastrar `LocalPlayer` y `RemotePlayer` |
| Separar Al Aparecer | activado, separación `4` |

En **Event Client**: `Server Url` = `http://localhost:5006`, `Heredar Delta` **activado**
(así un solo Δt gobierna las dos partes) y `Position Client` apuntando al propio
`NetworkManager`.

En **Arena Events** y **Experiment Runner**: arrastrar el propio `NetworkManager` a los
campos de referencia.

**Los identificadores no se tocan entre una instancia y otra.** Cada ventana deduce su papel
del argumento `-name PlayerN` que Multiplayer Play Mode pasa a cada editor: el principal
queda como `p1` y el jugador virtual como `p2`. Lo que no venga por argumento conserva el
valor del Inspector.

## Ejecutar las dos instancias

1. Levantar los dos servicios (ver el README de la raíz).
2. *Window → Multiplayer → Multiplayer Play Mode*, marcar **Player 2** como activo.
3. Play. Unity abre una segunda ventana, que es un proceso de editor independiente.

Cada ventana rotula arriba a qué jugador controla. Al principio muestran
`esperando primera posicion (404)` hasta que la otra publica: es el comportamiento correcto,
el 404 no detiene el sondeo.

> Si Multiplayer Play Mode falla al arrancar con un error sobre `Library/VP/...`, cerrar
> Unity y borrar la carpeta `unity-client/Library/VP/`. Unity la regenera.

## Controles

| Tecla | Qué hace |
|---|---|
| **WASD** | Mueve la cápsula propia |
| **Espacio** | `ProjectileFired`: una esfera sale hacia el otro jugador |
| **E** | `ShieldRaised`: escudo esférico translúcido durante 2 s |
| — | `PlayerHit` se emite solo cuando el proyectil alcanza a alguien, y lo evalúa **solo** la instancia que disparó |

## Experimentos

| Tecla | Experimento | En qué instancia |
|---|---|---|
| **F1** | A: 100 tiempos de ida y vuelta | en la que mide |
| **F2** | B: emite `posX` = 1…20, uno cada 20 ms | la emisora |
| **F3** | B: observa cuántos valores distintos llegan | la observadora |
| **F4** | C: emite 20 eventos, uno cada 20 ms | la emisora |
| **F5** | C: observa cuántos eventos llegan | la observadora |

Para B y C: pulsar **primero F3 (o F5) en la que observa** y enseguida **F2 (o F4) en la que
emite**. La observadora espera hasta 60 s a que empiece la emisión, de sobra para cambiar de
ventana, y termina sola tras un silencio proporcional a Δt.

Cada experimento se repite con `Delta Time Ms` en **50**, **200** y **1000**. El valor se
puede cambiar con el juego en marcha: el ciclo lo relee en cada vuelta.

Durante la medición el script desactiva el VSync y sube `targetFrameRate` a 500, para que la
cuantización por fotograma no contamine los tiempos.

Los CSV salen en `resultados/`, con una fila por muestra y una cabecera que resume la corrida.

El campo `Output Folder` del `ExperimentRunner` se deja **vacío**: en ese caso los CSV van a
la carpeta `resultados/` de la raíz del repositorio, se clone donde se clone. Solo hay que
llenarlo para escribir en otro sitio.
