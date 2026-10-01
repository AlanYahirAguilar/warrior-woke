# Dynamic Parkour System — material de terceros

- **Autor:** Èric Canela ([repositorio original](https://github.com/knela96/Dynamic-Parkour-System)).
- **Licencia:** MIT (ver [`LICENSE.txt`](LICENSE.txt), que debe acompañar a estos archivos).
- **Animaciones:** de [Mixamo](https://www.mixamo.com/), incluidas en el paquete original.
- **Copia local de origen:** `Recursos/Dynamic-Parkour-System-main` (Unity 2019.4, fuera del repo).

## Qué se copió

Solo las 11 animaciones que usa el jugador, con sus `.meta` originales (rangos de clip y curvas,
por ejemplo `LHandCurve` en *VaultFence*):

| Archivo | Clip | Uso en el proyecto |
|---|---|---|
| `Walk.fbx` | Walk | Blend de locomoción (1.7 m/s medidos) |
| `Jog Forward.fbx` | Jog Forward | Blend de locomoción (2.6 m/s medidos) |
| `Run.fbx` | Run | Blend de locomoción (correr, 5 m/s) |
| `Fall Idle.fbx` | Fall A Loop | Estado `Fall` |
| `Falling To Landing.fbx` | Falling To Landing | Aterrizaje sin input |
| `Land To Run Forward.fbx` | Fall A Land To Run Forward | Aterrizaje con input |
| `VaultFence.fbx` | Vault1 | Vault (con IK de mano) |
| `Slide.fbx` | Running Slide | Slide |
| `Idle To Braced Hang.fbx` | Idle To Braced Hang | Agarrarse de la cornisa |
| `Braced Hanging Idle.fbx` | Hanging Idle | Colgado de la cornisa |
| `Braced Hang Climb.fbx` | Braced Hang To Crouch | Subir la cornisa |

`Tools → Warrior Woke → Configurar Animaciones del Jugador` los reimporta como Humanoid con Avatar
propio (el original copiaba el Avatar del modelo Erika, que no se importó) y hornea el root motion
en la pose.

## Qué no se copió

Ningún script, prefab, escena, modelo ni material. La lógica de vault y auto step se adaptó a la
FSM del jugador (`EnvironmentChecker.TryFindVault`, `PlayerVaultState`, `PlayerMovement.TryAutoStep`),
con créditos en sus comentarios. Detalle de lo usado y lo descartado: `docs/arquitectura.md` §5.12.
