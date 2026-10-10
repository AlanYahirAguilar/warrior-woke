# Dynamic Parkour System — material de terceros

- **Autor:** Èric Canela ([repositorio original](https://github.com/knela96/Dynamic-Parkour-System)).
- **Licencia:** MIT (ver [`LICENSE.txt`](LICENSE.txt), que debe acompañar a estos archivos).
- **Animaciones:** de [Mixamo](https://www.mixamo.com/), incluidas en el paquete original.
- **Copia local de origen:** `Recursos/Dynamic-Parkour-System-main` (Unity 2019.4, fuera del repo).

## Qué se copió

Solo las animaciones que usa el jugador (9 desde el 2026-10-09: `Slide.fbx` se eliminó el 2026-10-02 al
reemplazar el slide por los clips de Quaternius, cuyo bucle sí es continuo, y `VaultFence.fbx` el
2026-10-09, al pasar el vault a los clips de mocap de Kinematica, P36), con sus `.meta` originales
(rangos de clip):

| Archivo | Clip | Uso en el proyecto |
|---|---|---|
| `Walk.fbx` | Walk | Blend de locomoción (1.7 m/s medidos), en el sitio |
| `Jog Forward.fbx` | Jog Forward | Blend de locomoción (2.6 m/s medidos), en el sitio |
| `Run.fbx` | Run | Blend de locomoción (correr, 5 m/s), en el sitio |
| `Fall Idle.fbx` | Fall A Loop | Estado `Fall` |
| `Falling To Landing.fbx` | Falling To Landing | Aterrizaje ligero sin input, medio y fuerte |
| `Land To Run Forward.fbx` | Fall A Land To Run Forward | Aterrizaje ligero con input |
| `Idle To Braced Hang.fbx` | Idle To Braced Hang | Agarrarse de la cornisa, con root motion |
| `Braced Hanging Idle.fbx` | Hanging Idle | Colgado de la cornisa |
| `Braced Hang Climb.fbx` | Braced Hang To Crouch | Subir la cornisa, con root motion |

`Tools → Warrior Woke → Configurar Animaciones del Jugador` los reimporta como Humanoid con Avatar
propio (el original copiaba el Avatar del modelo Erika, que no se importó) y fija el root motion de
cada clip: la locomoción, el aire y los aterrizajes se reproducen en el sitio; el agarre y la subida
conservan su root motion, que el juego aplica y warpea con `MatchTarget` (`docs/arquitectura.md` §5.10).
(Hoy la locomoción la hace motion matching; Walk, Jog y Run solo se ven en la mezcla de MxM con el
Animator Controller.)

**Por qué se dejó *VaultFence* (P36):** medido sobre Ch45, su vuelo caía como con una gravedad de ~4.2
m/s² (cámara lenta), aterrizaba a ~2.6 m del obstáculo y era un solo vault de carrera de una mano para
cualquier velocidad y obstáculo. Su curva `LHandCurve` se había perdido en una reimportación (fallo de
`ModelImporter.clipAnimations` en Unity 6000.6, T22).

## Qué no se copió

Ningún script, prefab, escena, modelo ni material. Se adaptaron a la FSM del jugador la detección del
vault (`EnvironmentChecker.TryFindVault`; el vault en sí es hoy el de P36), la del braced hang con `MatchTarget` e
IK (`EnvironmentChecker.TryFindLedge`, `PlayerLedgeGrabState`, `PlayerLedgeClimbState`,
`PlayerContactIK`), del root motion por estado (`PlayerAnimator`), del auto step
(`PlayerMovement.TryAutoStep`) y del IK de pies (`PlayerContactIK`), con créditos en sus comentarios.
Detalle de lo usado y lo descartado: `docs/arquitectura.md` §5.12.
Los límites de detección (alturas, alcances, fondos) ya no son campos sueltos como en el original:
viven en el Parkour Obstacle Standard (`scripts/Parkour/ParkourStandard.cs`, `docs/arquitectura.md`
§5.13), del que también salen los prefabs de obstáculos.
