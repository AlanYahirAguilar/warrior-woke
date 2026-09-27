# Cambios realizados — sesión de setup de repo, cámara y personaje

Este documento explica **qué se hizo y por qué**, en orden cronológico, para que quede claro el
razonamiento detrás de cada cambio (no solo el diff). Para el detalle línea por línea, revisa
`git diff` / `git log` — esto es el "por qué", no un sustituto de eso.

## 1. Diagnóstico: "hago pull y no veo los cambios de mi compañero"

**Síntoma:** después de `git pull`, Unity no mostraba el trabajo nuevo.

**Investigación:** el repo en sí estaba sano — `git status` limpio, sin archivos `.meta`
faltantes, `Force Text` ya activado en `ProjectSettings/EditorSettings.asset`. El problema real:
Unity recuerda qué escena tenías abierta en la carpeta `Library/` (local, en `.gitignore` a
propósito), así que un `git pull` que trae una escena nueva no cambia qué escena ves en el
Editor. Además, el proyecto tiene una escena señuelo (`CartoonLowPolyCityLite_01.unity`, la demo
del asset pack de entorno) fácil de confundir con "no hay nada implementado".

**Solución:**
- `Assets/scripts/Editor/SceneAutoLoader.cs`: al abrir el proyecto, si la escena activa no es
  `Level-1.unity`, la carga sola. Solo actúa una vez por sesión del Editor (`SessionState`) para
  no interrumpir si abres otra escena a propósito durante el trabajo.
- `CONTEXTO.md`: documenta cuál escena es la real y por qué pasa esto.

**Bug propio detectado y corregido:** la primera versión usaba `EditorApplication.isBatchMode`,
que no existe — la API correcta es `Application.isBatchMode` (namespace `UnityEngine`, no
`UnityEditor`). Eso rompía la compilación de **todos** los scripts del proyecto hasta que se
corrigió.

## 2. Higiene de git para trabajo en equipo con Unity

- **`.gitattributes` (nuevo):** normaliza fin de línea a LF, marca `.unity`/`.prefab`/`.mat`/etc.
  para usar el merge driver de Unity (`UnityYAMLMerge`) en vez de diff de texto plano, y marca
  binarios (imágenes, audio, `.fbx`, etc.) como binarios.
- **Merge driver de Unity registrado localmente** (`git config merge.unityyamlmerge.*`) — resuelve
  mucho mejor los conflictos en escenas/prefabs que un merge de texto genérico. Es un `git config`
  local a esta máquina, no algo que viaje en el repo (cada quien lo registra una vez, ver
  `CONTEXTO.md` sección 7).
- Se evaluó activar Git LFS para los `.fbx`/lighting data pesados que ya existen, pero se decidió
  **no activarlo todavía**: el repo está lejos del límite de 100MB de GitHub, y LFS obligaría a
  todo el equipo a instalar una herramienta extra. Queda documentado como paso opcional a futuro.

## 3. Movimiento libre en 3D + cámara al hombro (estilo Sleeping Dogs)

**Pedido:** reemplazar el modelo placeholder por un personaje real, cámara sobre el hombro, y
movimiento libre con teclado.

**Hallazgo antes de tocar código:** el juego estaba armado como plataformero 2.5D — Rigidbody con
`FreezePositionZ`, un solo eje de input, rotación instantánea 90°/-90°. Una cámara al hombro real
implica moverse libre en X/Z, lo cual choca con esa arquitectura. Se confirmó con el usuario cuál
alcance quería antes de tocar nada (ver pregunta de alcance) — se optó por migrar a 3D libre y
**deshabilitar temporalmente** vault/ledge-grab/wall-jump en vez de reescribirlos también, porque
esos estados dependen de raycasts en un eje mundial fijo (`Vector3.left`/`right`) sin un
equivalente claro en un mundo abierto.

**Cambios de código:**
- `IInputProvider.cs` / `PlayerInputHandler.cs` / `Player.cs`: se agregó `VerticalMove` (W/S o
  ↑/↓) junto al `HorizontalMove` que ya existía (A/D o ←/→).
- `PlayerMovement.cs`: reescrito — el movimiento ahora se calcula proyectando el input sobre el
  forward/right "aplanado" (sin Y) de `Camera.main`, en vez de un solo eje mundial. El
  `Rigidbody` ya no congela Z (`RigidbodyConstraints.FreezeRotation` en vez de
  `FreezePositionZ | FreezeRotation`). `SetVelocity` ahora tiene una sobrecarga que acepta un
  `Vector3` horizontal completo (X y Z), y se mantuvo la sobrecarga de un solo float para los
  casos donde un estado solo necesita "detenerse en seco".
- Rotación del personaje: primero se implementó con `Quaternion.RotateTowards` a 720°/seg: se
  sentía instantánea, y como la cámara persigue el `transform.forward` del jugador, cualquier
  cambio de dirección hacía que la cámara "girara muy rápido" (reportado por el usuario tras
  probarlo). Se cambió a `Mathf.SmoothDampAngle` (suaviza aceleración/desaceleración del giro en
  vez de una velocidad angular fija) — es el patrón estándar para este tipo de controlador de
  cámara en tercera persona.
- `PlayerGroundedStates.cs` / `PlayerAirStates.cs` / `PlayerCombatStates.cs`: las transiciones a
  Vault/LedgeGrab/LedgeClimb/WallJump quedaron **comentadas** (no borradas) con una nota explicando
  por qué. Slide y Dodge ahora dashean hacia `transform.forward` en vez de un escalar ±1. Las
  condiciones de "¿hay input?" pasaron de `Mathf.Abs(InputX) > 0.1f` a `player.HasMoveInput`
  (considera ambos ejes).
- `CameraFollow.cs`: reescrito de cámara lateral fija a cámara al hombro — se posiciona
  detrás/arriba de un hombro configurable (`shoulderHeight`, `shoulderSide`, `distance`,
  `lookAheadDistance`), sigue con `SmoothDamp`/`Slerp`, y hace un `SphereCast` para acercarse si
  hay pared entre la cámara y el personaje. Sigue el patrón ya existente de auto-detectar al
  Player vía el evento `Player.OnPlayerSpawned` (cero configuración manual en el Inspector).

## 4. Pipeline de reemplazo de modelo del personaje

**Por qué un script de Editor y no edición directa del prefab:** reemplazar el mesh de un
personaje implica que Unity importe el `.fbx` (generar el Avatar Humanoid, calcular los bounds
reales del modelo) — eso solo se puede hacer con el motor de importación de Unity corriendo, no
editando YAML a mano. Por eso se optó por un comando de menú que corre dentro del Editor del
usuario en vez de que el asistente edite `Player.prefab` directamente.

- `Assets/scripts/Editor/PlayerCharacterSetup.cs` (menú **Tools → Warrior Woke → Configurar
  Modelo del Jugador**):
  1. Configura el `.fbx` como rig Humanoid (cae a Generic si no tiene un esqueleto humano válido).
  2. Quita el `MeshFilter`/`MeshRenderer` placeholder del root de `Player.prefab`.
  3. Instancia el modelo como hijo (`Model`), calculando sus bounds reales
     (`Renderer.bounds` combinados).
  4. Si el modelo viene en una escala fuera de rango humano (menos de 0.5 o más de 4 unidades de
     alto), lo reescala a ~1.8m — cubre el caso común de un FBX exportado en centímetros.
  5. Centra el modelo para que su medio vertical caiga en el origen local del root (la misma
     convención que ya usaba el placeholder: root = centro del torso, no los pies) y ajusta
     `CapsuleCollider.height/radius` y la posición de `HeadPoint` al tamaño real. `GroundChecker`
     ya lee `capsuleCollider.bounds.min.y` en tiempo real, así que esto es lo único necesario para
     que el personaje quede parado en el piso correctamente.
  6. Es idempotente: se puede volver a correr sin dejar modelos viejos pegados.
- Se copió el modelo del usuario (`character.fbx`, después confirmado que es el mismo archivo que
  `Protagonista.fbx` — el usuario lo había renombrado) a `Assets/Characters/Player/character.fbx`.

## 5. Suelo y punto de spawn

**Síntoma:** el jugador "se encuentra en el edificio" — no había forma de bajarlo a nivel de
calle.

**Investigación en `Level-1.unity`:** el Player no está colocado directamente en la escena — lo
spawnea un `Spawner` (vía `ObjectPoolManager`, ver `Assets/scripts/Core/Spawning/`) apenas arranca
la escena, en la posición del propio `Spawner`. Ese `Spawner` estaba en **`(0, 40, -2.5)`** — 40
unidades en el aire — mientras que los dos edificios de LowPolyCity en la escena
(`House_01_cyber` en `(0,0,0)` y `House_01_cyber_2` en `(35,2,0)`) están a nivel de piso. No
existía ningún suelo real bajo el punto de spawn: el prefab `initial_floor` (un cuarto de
bloqueo viejo, de antes de meter los assets de LowPolyCity) nunca se colocó en esta escena.

**Solución:**
- Se agregó un GameObject **`Ground`** directamente en `Level-1.unity`: un plano de 100x100
  unidades (mesh cubo built-in de Unity, igual que usaba el placeholder del jugador), material
  `floors.mat` (ya existía en el proyecto), con su superficie superior exactamente en `Y = 0`,
  centrado para cubrir ambos edificios. Está en el **Layer "Ground"** (capa 6) — el mismo que ya
  usan los edificios y que `GroundChecker` ya escaneaba (`groundLayer` incluye las capas Ground y
  Obstacle), así que no hizo falta tocar ningún script para que lo detecte.
- Se bajó la posición Y del `Spawner` de `40` a `2`, para que el jugador aparezca justo encima del
  nuevo suelo (una caída corta y segura) en vez de a 40 unidades de altura.

**Nota de riesgo:** este cambio edita `Assets/Scenes/Level-1.unity` directamente mientras el
Editor del usuario la tenía abierta. Si Unity no recarga solo el archivo, hay que reabrir la
escena manualmente antes de seguir editando — si se guarda desde el Editor con la versión vieja
todavía en memoria, se perderían estos cambios.

## 6. Documentación

- `CONTEXTO.md` (raíz): guía de onboarding — cómo abrir el proyecto, qué escena usar, git
  workflow, y ahora la sección 4 con controles/cámara/por qué el parkour está deshabilitado.
- `docs/CONTEXTO_JUEGO.md`: de qué trata el juego (inferido del código — no hay un GDD en el
  repo).
- `docs/ARQUITECTURA.md`: cómo está armado técnicamente (patrones, capas, componentes).
- `docs/CAMBIOS.md`: este documento.
