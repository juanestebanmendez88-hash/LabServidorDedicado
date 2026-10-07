# Scripts del cliente Unity

Estos archivos se copian a `unity-client/Assets/Scripts/` una vez creado el proyecto.
Están aquí aparte porque Unity Hub exige una carpeta vacía para crear el proyecto.

| Archivo | Qué hace |
|---|---|
| `PositionData.cs` | Cuerpo JSON del servicio de referencia (`posX`, `posY`, `posZ`) |
| `PositionSyncClient.cs` | Cliente de la Parte 1: publica y consulta posiciones |
| `PlayerController.cs` | Movimiento del jugador local con WASD |
| `ExperimentRunner.cs` | Experimentos A y B, con salida a CSV |

## Montaje en Unity

**1. Crear el proyecto.** Unity Hub → New project → plantilla **Universal 3D** → nombre `unity-client` → ubicación `LabServidorDedicado`.

**2. Permitir el Input Manager antiguo.** `Edit → Project Settings → Player → Other Settings → Active Input Handling` = **Both**. Unity pide reiniciar.

**3. Permitir HTTP.** En la misma pantalla, `Allow downloads over HTTP` = **Always allowed**. Sin esto Unity bloquea todas las peticiones.

**4. Copiar los scripts** a `Assets/Scripts/`.

**5. Armar la escena** (menú `GameObject`):

| Objeto | Cómo se crea | Ajustes |
|---|---|---|
| Piso | `3D Object → Plane` | — |
| `LocalPlayer` | `3D Object → Capsule` | Position Y = 1 |
| `RemotePlayer` | `3D Object → Capsule` | Position X = 2, Y = 1, material rojo |
| `NetworkManager` | `Create Empty` | lleva los dos scripts de red |

**6. Asignar los componentes.**

En **LocalPlayer**: `Add Component → Player Controller`.

En **NetworkManager**: `Add Component → Position Sync Client` y `Add Component → Experiment Runner`.

**7. Llenar el Inspector de NetworkManager.**

En *Position Sync Client*:

| Campo | Instancia A (build) | Instancia B (editor) |
|---|---|---|
| Server Url | `http://localhost:5005` | `http://localhost:5005` |
| Game Id | `g1` | `g1` |
| Local Player Id | `p1` | `p2` |
| Remote Player Id | `p2` | `p1` |
| Delta Time Ms | `200` | `200` |
| Local Player | arrastrar **LocalPlayer** | igual |
| Remote Player | arrastrar **RemotePlayer** | igual |

Los identificadores van **cruzados**: lo que para una instancia es local, para la otra es remoto.

En *Experiment Runner*, arrastrar el propio **NetworkManager** al campo `Client`.

**8. Guardar la escena** como `Assets/Scenes/Main.unity`.

## Ejecutar las dos instancias

1. `File → Build Settings → Add Open Scenes`, y compilar a `LabServidorDedicado/build-p1/`. Ese build queda con `p1`.
2. Volver al editor y cambiar en el Inspector `Local Player Id` a `p2` y `Remote Player Id` a `p1`.
3. Abrir el build y darle Play al editor.

Al principio cada instancia muestra `esperando primera posicion (404)` hasta que la otra publica. Es el comportamiento correcto: el 404 no detiene el sondeo.

## Experimentos

Con las dos instancias corriendo:

| Tecla | Qué hace |
|---|---|
| **F1** | Experimento A: 100 RTT con el Δt configurado |
| **F3** | Experimento B: deja esta instancia observando |
| **F2** | Experimento B: emite `posX` = 1…20, uno cada 20 ms |

**Experimento A:** poner `Delta Time Ms` en 50, pulsar F1, esperar. Repetir con 200 y 1000.

**Experimento B:** pulsar **F3 en la instancia que observa** y enseguida **F2 en la que emite**. Repetir con los tres Δt.

Los CSV salen en `LabServidorDedicado/resultados/`.

## Valores de referencia

El experimento B se simuló contra el servicio real antes de integrarlo con Unity:

| Δt | Valores distintos observados (de 20) |
|---|---|
| 50 ms | 11 |
| 200 ms | 5 |
| 1000 ms | 1 |

Desde Unity los números serán parecidos pero no idénticos, porque las corrutinas añaden la cuantización por fotograma. Si salen muy distintos, revisar que el VSync esté desactivado durante la medición.
