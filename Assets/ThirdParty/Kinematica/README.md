# Kinematica Demo — mocap de Unity Technologies

- **Autor:** Unity Technologies ([repositorio original](https://github.com/Unity-Technologies/Kinematica_Demo)).
- **Licencia:** **Unity Companion License** (ver [`LICENSE.md`](LICENSE.md)). Permite usar, modificar y
  distribuir el material **dentro de proyectos hechos con Unity**; no se puede usar con otros motores.
- **Copia local de origen:** `Recursos/Kinematica_Demo` (fuera del repo), clonada el 2026-10-05 con
  `git lfs` (los FBX están en LFS: la descarga en ZIP de GitHub no los incluye).
- **Versionado:** los FBX de esta carpeta van por **Git LFS** (P35, `.gitattributes`). Instala Git LFS
  antes de clonar o hacer pull, o llegarán como punteros de texto.
- **Contenido del original:** 112 tomas de mocap continuo del actor `Unit` (más sus espejos `_M`),
  pensadas para motion matching: arranques y frenadas, giros con pie plantado, círculos y eslálones a
  paso, trote y sprint, vaults, subidas a cornisa, escaladas y wall runs. **No** trae caminar hacia
  atrás, strafe ni combate.

## Estado: ✅ en uso (locomoción por motion matching y vaults)

Un subconjunto de 25 tomas, retargeteado a Ch45:

- **Locomoción** (desde el 2026-10-08): 12 tomas de locomoción y `Idle` forman la base de MxM
  (`Assets/Data/MxM`, ver `docs/arquitectura.md` §7.2) con la que camina, corre y esprinta el jugador.
- **Vaults** (desde el 2026-10-09, decisión P36): 7 vaults anotados de las tomas `Vaults_*` (y sus
  espejos) son los clips del vault, elegidos y medidos por **Tools → Warrior Woke → Construir Catálogo de
  Vaults** (`VaultCatalogBuilder`) a partir de las anotaciones del Kinematica Demo (`Unit.asset`: tramos
  de vault, contactos `Hand_L`/`Hand_R`/`FootBridge` y marcador de escape). El catálogo
  (`Assets/Data/Parkour/VaultCatalog.asset`) guarda para cada uno su trayectoria, su orientación, los
  apoyos de las manos y en qué obstáculos cabe; ver `docs/arquitectura.md` §5.17.

La herramienta de retarget es **Tools → Warrior Woke → Probar Retarget del Mocap**
(`Assets/scripts/Editor/MocapRetargetProbe.cs`), que también prueba `ThirdParty/100STYLE` y `ThirdParty/CMU`.

| Carpeta | Contenido | Importación |
|---|---|---|
| `Character/Unit.FBX` | Modelo del actor del mocap | Humanoid con su propio Avatar (mapeo automático; sin `Eye_L/R` ni `Jaw`, que los clips no tienen) |
| `Animations/*.fbx` | 25 tomas (abajo) | Humanoid copiando el Avatar de `Unit`, con root motion completo (sin bloquear rotación ni posición). Las tomas de locomoción de la base de motion matching llevan la altura horneada en la pose (basada en los pies); las de parkour conservan la altura en la raíz (`MxMLocomotionBuilder.IsGroundLocomotion`). Las tomas de vault llevan además los sub-clips `Vault_<id>` y `Vault_<id>_M` (espejo) del catálogo, con root motion en XZ, la altura horneada y el giro de la raíz libre (el warper lo reemplaza) |

Tomas copiadas: `Idle`, `Acceleration`, `Start_Stop_1`, `Start_Stop_2`, `Stop_to_Face_1`,
`Plants_Turns_Regular_1`, `Plants_Turns_Fancy_1`, `Circles_Walk_1`, `Circles_Jog_1`,
`Circles_Sprint_1`, `Circles_Sprint_2`, `Snakes_Walk`, `Snakes_Jog`, `Snakes_Sprint`,
`Vaults_Over_Ledge_Jog`, `Vaults_Over_Ledge_Sprint`, `Vaults_Over_Table_1`, `Vaults_Sliding_Jog`,
`Climb_to_Ledge_Stand`, `Climb_Up_Wall_1`, `Ledge_Idle`, `Parkour_Climbing_Sprint_1` (2026-10-05) y,
para el vault (2026-10-08), `Vaults_Over_Ledge_Walk`, `Vaults_Over_Table_2` y
`Vaults_Sliding_Stand_Walk_1`. También se copiaron y midieron `Vaults_Sliding_Sprint_1`,
`Vaults_Sliding_Sprint_2` y `Vaults_Sliding_Stand_Walk_2`; ninguna marcha las necesitó y se eliminaron
(P8).

| Vault (sub-clip) | Toma | Estilo | Carrera de entrada (medida) | Velocidades que acepta |
|---|---|---|---|---|
| `LedgeWalkB` | `Vaults_Over_Ledge_Walk` | rápido (una mano, el cuerpo de lado) | 1.3 m/s | parado a 1.7 m/s |
| `SlidingStand1` | `Vaults_Sliding_Stand_Walk_1` | lento (las manos y la cadera sobre la cima) | 2.3 m/s | parado a 3.2 m/s |
| `LedgeJogA` | `Vaults_Over_Ledge_Jog` | rápido | 2.6 m/s | parado a 3.5 m/s |
| `SlidingJogA` | `Vaults_Sliding_Jog` | lento | 3.7 m/s | 2.2–5.0 m/s |
| `LedgeSprintB` | `Vaults_Over_Ledge_Sprint` | rápido | 5.0 m/s | 3.0–6.7 m/s |
| `TableB` | `Vaults_Over_Table_1` | dive (las manos, de cabeza) | 4.8 m/s | 2.9–6.5 m/s |
| `Table2B` | `Vaults_Over_Table_2` | dive | 4.2 m/s | 2.5–5.7 m/s; despega a 0.66 m de la mano (los demás a 1.4–1.8 m corriendo): admite un Espacio tardío |

Las velocidades que acepta son 0.6–1.35 veces su carrera de entrada (desde 0 las de menos de 2.6 m/s);
el planificador elige además por obstáculo, distancia y pie adelantado (`docs/arquitectura.md` §5.17).

Los FBX originales venían como rig **Generic**; se importan como Humanoid para retargetearlos a Ch45.

## Resultado de la prueba (2026-10-05)

- El Avatar de `Unit` mapea todos los huesos Humanoid, incluidos los dedos. Los únicos avisos son
  del hueso intermedio `FootBridge` (0.3–1.5° de diferencia), despreciables.
- **Con Foot IK** (opción del estado o del playable), Ch45 patina lo mismo que el actor original
  (mediana del punto de apoyo 0.04–0.19 m/s frente a 0.04–0.15) y las suelas quedan sobre el suelo.
  **Sin Foot IK**, el retarget patina hasta 1.4 m/s y hunde las suelas hasta 8 cm: el Foot IK es
  obligatorio.
- Velocidades del mocap: caminar ~1.1–1.6 m/s, trote ~3.2 m/s y sprint con punta de ~4.5–5.1 m/s.
- Las hojas de poses (`Logs/MocapProbe/Kinematica/*.png`, arriba `Unit` y abajo Ch45) muestran la misma
  pose en los dos personajes, también en vaults y giros.
