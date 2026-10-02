# Dynamic Parkour System — material de terceros

- **Autor:** Èric Canela ([repositorio original](https://github.com/knela96/Dynamic-Parkour-System)).
- **Licencia:** MIT (ver [`LICENSE.txt`](LICENSE.txt), que debe acompañar a estos archivos).
- **Animaciones:** de [Mixamo](https://www.mixamo.com/), incluidas en el paquete original.
- **Copia local de origen:** `Recursos/Dynamic-Parkour-System-main` (Unity 2019.4, fuera del repo).

## Qué se copió

Solo las 11 animaciones que usa el jugador, con sus `.meta` originales (rangos de clip):

| Archivo | Clip | Uso en el proyecto |
|---|---|---|
| `Walk.fbx` | Walk | Blend de locomoción (1.7 m/s medidos), en el sitio |
| `Jog Forward.fbx` | Jog Forward | Blend de locomoción (2.6 m/s medidos), en el sitio |
| `Run.fbx` | Run | Blend de locomoción (correr, 5 m/s), en el sitio |
| `Fall Idle.fbx` | Fall A Loop | Estado `Fall` |
| `Falling To Landing.fbx` | Falling To Landing | Aterrizaje ligero sin input, medio y fuerte |
| `Land To Run Forward.fbx` | Fall A Land To Run Forward | Aterrizaje ligero con input |
| `VaultFence.fbx` | Vault1 | Vault, con root motion y la curva `LHandCurve` (IK de la mano) |
| `Slide.fbx` | Slide Down · Slide · Slide Up | Slide en tres fases (bajar, deslizar en bucle, levantarse) |
| `Idle To Braced Hang.fbx` | Idle To Braced Hang | Agarrarse de la cornisa, con root motion |
| `Braced Hanging Idle.fbx` | Hanging Idle | Colgado de la cornisa |
| `Braced Hang Climb.fbx` | Braced Hang To Crouch | Subir la cornisa, con root motion |

`Tools → Warrior Woke → Configurar Animaciones del Jugador` los reimporta como Humanoid con Avatar
propio (el original copiaba el Avatar del modelo Erika, que no se importó) y fija el root motion de
cada clip: la locomoción, el aire, los aterrizajes y el slide se reproducen en el sitio; el vault,
el agarre y la subida conservan su root motion, que el juego aplica y warpea con `MatchTarget`
(`docs/arquitectura.md` §5.10). La curva `LHandCurve` de *VaultFence* se perdió en una reimportación
(fallo de `ModelImporter.clipAnimations` en Unity 6000.6) y la herramienta la restaura con las claves
del `.meta` original.

## Qué no se copió

Ningún script, prefab, escena, modelo ni material. Se adaptaron a la FSM del jugador la lógica de
vault (`EnvironmentChecker.TryFindVault`, `PlayerVaultState`), del braced hang con `MatchTarget` e
IK (`EnvironmentChecker.TryFindLedge`, `PlayerLedgeGrabState`, `PlayerLedgeClimbState`,
`PlayerContactIK`), del root motion por estado (`PlayerAnimator`), del auto step
(`PlayerMovement.TryAutoStep`) y del IK de pies (`PlayerContactIK`), con créditos en sus comentarios.
Detalle de lo usado y lo descartado: `docs/arquitectura.md` §5.12.
