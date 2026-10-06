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

## Estado: 🔧 prueba de retarget (decisión del 2026-10-05: motion matching con MxM)

Por ahora solo hay un subconjunto de 22 tomas para comprobar que el mocap se retargetea bien sobre
Ch45. Todavía **no** las usa el jugador. La herramienta es **Tools → Warrior Woke → Probar Retarget del
Mocap** (`Assets/scripts/Editor/MocapRetargetProbe.cs`), que también prueba `ThirdParty/100STYLE`.

| Carpeta | Contenido | Importación |
|---|---|---|
| `Character/Unit.FBX` | Modelo del actor del mocap | Humanoid con su propio Avatar (mapeo automático; sin `Eye_L/R` ni `Jaw`, que los clips no tienen) |
| `Animations/*.fbx` | 22 tomas (abajo) | Humanoid copiando el Avatar de `Unit`, con root motion completo (sin bloquear rotación, altura ni posición) |

Tomas copiadas: `Idle`, `Acceleration`, `Start_Stop_1`, `Start_Stop_2`, `Stop_to_Face_1`,
`Plants_Turns_Regular_1`, `Plants_Turns_Fancy_1`, `Circles_Walk_1`, `Circles_Jog_1`,
`Circles_Sprint_1`, `Circles_Sprint_2`, `Snakes_Walk`, `Snakes_Jog`, `Snakes_Sprint`,
`Vaults_Over_Ledge_Jog`, `Vaults_Over_Ledge_Sprint`, `Vaults_Over_Table_1`, `Vaults_Sliding_Jog`,
`Climb_to_Ledge_Stand`, `Climb_Up_Wall_1`, `Ledge_Idle`, `Parkour_Climbing_Sprint_1`.

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
