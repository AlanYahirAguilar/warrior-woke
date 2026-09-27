# Warrior Woke — Contexto del proyecto

Juego de acción en tercera persona hecho en **Unity 6000.6.0f1 (Unity 6)** con **URP (Universal
Render Pipeline)**. Cámara al hombro (over-the-shoulder, estilo Sleeping Dogs) y movimiento
libre en 3D. El proyecto nació como plataformero 2.5D (plano fijo, izquierda/derecha) y está en
transición a este esquema — ver sección 4 para el detalle de qué quedó adaptado y qué quedó
pendiente.

Este documento existe para que cualquier persona que clone el repo entienda en qué va el
proyecto y pueda abrirlo sin sorpresas, sin necesitar que alguien le explique nada por chat.
Actualízalo cuando cambie algo importante (nueva mecánica grande, cambio de escena principal,
cambio de flujo de trabajo). No hace falta detallar cada commit, para eso está `git log`.

Documentos relacionados en `docs/`:
- [`docs/CONTEXTO_JUEGO.md`](docs/CONTEXTO_JUEGO.md) — de qué trata el juego (género, mecánicas, ambientación).
- [`docs/ARQUITECTURA.md`](docs/ARQUITECTURA.md) — cómo está armado el código (patrones, componentes, dónde vive cada cosa).
- [`docs/CAMBIOS.md`](docs/CAMBIOS.md) — qué se cambió en la última sesión grande de trabajo y por qué.

## 1. Requisitos para abrir el proyecto

- **Unity 6000.6.0f1** exacto, instalado vía Unity Hub (revisa `ProjectSettings/ProjectVersion.txt`
  si tienes dudas de la versión). Abrir el proyecto con otra versión puede disparar un reimport
  masivo y romper materiales/prefabs sin que sea culpa de nadie.
- Clona el repo, abre Unity Hub → **Add** → selecciona la carpeta `warrior-woke/` (la que
  contiene `Assets/`, `ProjectSettings/`, `Packages/`). La primera vez que se abre, Unity va a
  tardar varios minutos importando todo — es normal, déjalo terminar.

## 2. Qué hay hecho ahora mismo

- **Movimiento y estados del jugador**: máquina de estados en
  `Assets/scripts/Player/StateMachine/` con estados de suelo, aire y combate
  (`PlayerGroundedStates`, `PlayerAirStates`, `PlayerCombatStates`). El movimiento es libre en 3D
  y relativo a cámara — ver sección 4. Los estados de parkour (`PlayerParkourStates` y wall-jump)
  siguen en el código pero están deshabilitados (ver sección 4).
- **Combate**: `HealthSystem` y `Hitbox` en `Assets/scripts/Core/Combat/`.
- **Pooling y spawns**: `ObjectPoolManager` y `Spawner` en `Assets/scripts/Core/Spawning/`.
- **Cámara**: `CameraFollow` en `Assets/scripts/Camera/` — cámara al hombro (sección 4).
- **Prefabs clave**: `Player`, `Enemy`, `GameManager`, `Spawner`, `initial_floor` en
  `Assets/Prefabs/`.
- **Escena jugable**: `Assets/Scenes/Level-1.unity` (ver sección 3 — es la única escena "del
  juego", no la confundas con la demo del asset pack de entorno).
- **Entorno**: assets del pack `CartoonLowPolyCity` (`Assets/LowPolyCity/`) y animaciones/modelos
  low poly (`Assets/LowPoly/`), con lighting horneado para `Level-1`.
- Pipeline gráfico: URP ya configurado (quality settings, pipeline assets, materiales).

Para el detalle real y actualizado, `git log --oneline` es la fuente de verdad; esta lista es
solo el resumen de alto nivel.

## 3. Qué escena abrir (importante)

El proyecto tiene **dos** archivos `.unity`:

| Escena | Qué es |
|---|---|
| `Assets/Scenes/Level-1.unity` | **La escena del juego.** Jugador, enemigo, spawners, combate. Es la que está registrada en Build Settings. |
| `Assets/LowPolyCity/Scenes/CartoonLowPolyCityLite_01.unity` | Escena de **demostración del asset pack** de entorno. No tiene jugador ni lógica del juego. Fácil de confundir con "no hay nada implementado" si la abres por error. |

**El proyecto incluye un auto-loader de escena** (`Assets/scripts/Editor/SceneAutoLoader.cs`):
al abrir el proyecto en el Editor, si la escena activa no es `Level-1.unity`, la carga
automáticamente. Solo actúa **una vez por sesión del Editor** (usa `SessionState`), así que si
durante tu sesión abres otra escena a propósito (por ejemplo, la demo del asset pack para sacar
más piezas), el script no te la va a cerrar ni a interrumpir — solo vuelve a intervenir la
próxima vez que cierres y abras Unity desde cero.

Es un script de Editor puro (vive en una carpeta `Editor/`), no se incluye en builds.

## 4. Personaje y cámara al hombro

### Controles actuales

- **Movimiento**: WASD o flechas — libre en 3D, relativo a la cámara (adelante/atrás con W/S o
  ↑/↓, strafe con A/D o ←/→). El personaje rota suavemente hacia donde te mueves.
- **Salto**: Espacio · **Slide**: Shift · **Golpe ligero**: click izquierdo · **Golpe fuerte**:
  click derecho · **Bloqueo**: F (mantenido) · **Esquiva**: E.

### Cómo quedó armada la cámara

`CameraFollow.cs` (en el Main Camera) ya no es una cámara lateral fija: se posiciona detrás y
arriba de un hombro del jugador (offset configurable en el Inspector: `shoulderHeight`,
`shoulderSide`, `distance`, `lookAheadDistance`), sigue con suavizado (`SmoothDamp`/`Slerp`), y
hace un SphereCast para acercarse si hay una pared entre la cámara y el personaje (evita que la
cámara atraviese geometría). Sigue detectando al Player automáticamente (evento
`Player.OnPlayerSpawned`), no hace falta arrastrar nada a mano en el Inspector.

`PlayerMovement.cs` calcula la dirección de movimiento proyectando el input sobre el forward/right
"aplanado" (sin componente Y) de `Camera.main`. Por eso mover al personaje y que la cámara lo
siga son dos scripts independientes que se retroalimentan: la cámara sigue el `transform.forward`
del jugador, y el input del jugador se interpreta según hacia dónde mira la cámara.

### Por qué el parkour quedó deshabilitado

El juego se armó originalmente como plataformero 2.5D: el Rigidbody tenía la posición Z
congelada, y todo el movimiento era un solo eje (izquierda/derecha) con el personaje rotando
90°/-90°. Los estados de pared/cornisa (`PlayerVaultState`, `PlayerLedgeGrabState`,
`PlayerLedgeClimbState`, `PlayerWallJumpState` en `PlayerParkourStates.cs` /
`PlayerAirStates.cs`) y `EnvironmentChecker.cs` están construidos sobre ese plano fijo (raycasts
literalmente en `Vector3.left`/`Vector3.right`).

Al pasar a movimiento libre en 3D, esos raycasts ya no tienen un "adelante" fijo que revisar, así
que **las transiciones hacia esos estados se comentaron** (búscalas como
`// Parkour — disabled` en `PlayerGroundedStates.cs` y `PlayerAirStates.cs`). El código de los
estados sigue ahí completo — si más adelante se quiere retomar vault/ledge-grab/wall-jump en 3D,
hay que rehacer `EnvironmentChecker` para que revise en la dirección real (`transform.forward`)
en vez de un eje mundial fijo, y decidir cómo detectar cornisas/paredes en un mundo abierto. No es
un bug: fue una decisión de alcance para priorizar el movimiento libre + cámara al hombro.

Sí siguen funcionando: Idle/Run/Jump/Slide, y todo el combate (ataques, bloqueo, esquiva) — la
esquiva y el slide ahora dashean hacia `transform.forward` en vez de un signo ±1.

### Cómo poner un modelo de personaje nuevo

1. Copia el `.fbx` a `Assets/Characters/Player/` (o la subcarpeta que prefieras dentro de
   `Assets/Characters/`) y espera a que aparezca en la ventana Project (Unity lo importa solo).
2. Si copiaste un modelo distinto al que ya está (`character.fbx`), abre
   `Assets/scripts/Editor/PlayerCharacterSetup.cs` y ajusta la constante `ModelPath` a la nueva
   ruta.
3. Menú **Tools → Warrior Woke → Configurar Modelo del Jugador**. Esto:
   - Configura el modelo como rig **Humanoid** (o cae a Generic si el FBX no trae un esqueleto
     humano válido — revisa la Consola).
   - Quita el mesh placeholder (el cubo) de `Player.prefab` y mete el modelo nuevo como hijo
     (`Model`).
   - Si el modelo viene en una escala rarísima (muy chico o gigante), lo reescala a ~1.8 m.
   - Ajusta el `CapsuleCollider` y `HeadPoint` al tamaño real del modelo, para que el personaje
     quede parado en el piso correctamente (ni flotando ni hundido) — `GroundChecker` ya lee el
     collider en tiempo real, así que esto es todo lo que hace falta.
   - Es **idempotente**: puedes volver a correrlo (por ejemplo tras cambiar el FBX) sin dejar
     modelos viejos pegados.
4. Si el modelo trae animaciones (Idle/Run/etc.), quedan como `Animator` + `Avatar` en el hijo
   `Model` — falta crear un Animator Controller y wirearlo (hay comentarios `// TODO: trigger
   animation` en los estados de combate marcando dónde conectarlo).

## 5. Por qué a veces "hago pull y no veo nada"

Si hiciste `git pull` y el Editor no te muestra los cambios de tu compañero, casi siempre es
por una de estas dos razones (no es un problema del repo):

1. **Unity recuerda localmente qué escena tenías abierta**, y ese dato vive en la carpeta
   `Library/` — que está en `.gitignore` a propósito porque es una caché de importación
   específica de cada máquina, no algo que se deba compartir. Si tu compañero agregó una escena
   nueva, `git pull` la trae al disco, pero tu Editor sigue mostrando lo que tenías abierto
   antes, porque eso no es parte del commit. **El auto-loader de la sección 3 soluciona esto**
   para el caso más común (abrir el proyecto desde cero).
2. Abriste la escena equivocada (la demo del asset pack en vez de `Level-1.unity`) — ver
   sección 3.

Si después de eso el Editor muestra objetos rosa/magenta o "missing script", casi siempre es
Unity todavía reimportando (espera a que termine la barra de progreso) o una versión de Unity
distinta a `6000.6.0f1`.

## 6. Flujo de trabajo con git recomendado

- Antes de abrir Unity, haz `git pull`. Deja que Unity termine de reimportar antes de tocar nada.
- **Nunca edites a mano los archivos `.meta`** ni los borres/muevas fuera de Unity (mover o
  renombrar assets desde el Explorador de Windows rompe las referencias/GUIDs). Mueve y renombra
  siempre desde dentro del Editor (ventana Project).
- Si dos personas van a tocar la **misma escena o el mismo prefab** al mismo tiempo, avísense
  antes — los conflictos en archivos `.unity`/`.prefab` son molestos incluso con el merge
  inteligente configurado (sección 7). Cuando se pueda, que cada quien trabaje en una escena o
  prefab distinto.
- Antes de hacer commit, revisa `git status`: si ves cambios en `Library/`, `Temp/`, `Logs/`,
  `UserSettings/`, `obj/` o similares, algo está mal configurado (no debería pasar, están en
  `.gitignore`) — no los agregues.
- Convención de mensajes de commit que se ha venido usando: `feat: ...`, `fix(scope): ...`,
  `chore(scope): ...`. Síguela para que `git log` siga siendo legible.
- Con dos o más personas trabajando a la vez, considera usar una rama por feature en vez de
  commitear directo a `main`, sobre todo si van a tocar la misma escena.

## 7. Configura el merge inteligente de Unity (una sola vez por máquina)

Git, por defecto, intenta mezclar archivos de escena/prefab (`.unity`, `.prefab`, `.mat`, etc.)
como si fueran texto plano línea por línea. Unity trae su propia herramienta (`UnityYAMLMerge`)
que entiende la estructura YAML y resuelve mucho mejor los conflictos. El repo ya declara en
`.gitattributes` qué extensiones deben usarla, pero cada quien tiene que registrar la
herramienta **una vez** en su máquina (es una ruta local, no se puede compartir por git):

**Windows** (ajusta la versión si usas otra distinta a `6000.6.0f1`):

```
git config merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config merge.unityyamlmerge.driver "\"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Tools/UnityYAMLMerge.exe\" merge -p %O %A %B %A"
git config merge.unityyamlmerge.recursive binary
```

**macOS**:

```
git config merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Tools/UnityYAMLMerge' merge -p %O %A %B %A"
git config merge.unityyamlmerge.recursive binary
```

Ya está configurado en esta máquina. Es un `git config` local a este repo (no toca nada del
repo ni de tu compañero), así que no hay riesgo si lo corres de nuevo o si lo cambias.

## 8. `.gitignore` y `.gitattributes`

- **`.gitignore`**: excluye `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, archivos de
  IDE (`.vs/`, `.idea/`, `.vscode/`, `*.csproj`, `*.sln`, `*.slnx`), etc. Todo eso se regenera
  solo al abrir el proyecto — si algo de esa lista falta o "aparece cambiado" en `git status`,
  probablemente sea un problema de configuración, no algo que debas commitear.
- **`.gitattributes`**: normaliza fin de línea a LF para evitar diffs falsos entre
  Windows/macOS, marca los archivos YAML de Unity para usar el merge inteligente (sección 7), y
  marca binarios (imágenes, audio, modelos `.fbx`, etc.) como binarios para que git no intente
  diffearlos como texto.
- Serialización de Unity ya está en **Force Text** (`ProjectSettings/EditorSettings.asset`,
  `m_SerializationMode: 2`) — necesario para que escenas y prefabs sean diffeables/mergeables en
  git. No lo cambies a Binary.

## 9. Git LFS (a futuro, no activado todavía)

El repo ya tiene animaciones `.fbx` de varios MB (`Assets/LowPoly/Animations/`, más el
`character.fbx` de la sección 4) y datos de lighting pesados
(`Assets/Scenes/Level-1/LightingData.asset`, ~13 MB). Todavía está lejos del límite de GitHub
(100 MB por archivo), así que **no** se activó Git LFS para no meter una dependencia extra que
todo el equipo tendría que instalar. Si el repo empieza a crecer mucho en arte/audio/animaciones,
hay bloques comentados listos para usar al final de `.gitattributes` — descomentarlos y correr
`git lfs install` + `git lfs migrate import` (coordinándolo con todo el equipo, porque reescribe
el historial).

## 10. Estructura de carpetas relevante

```
Assets/
  scripts/
    Camera/          -> cámara al hombro (CameraFollow.cs)
    Core/
      Combat/        -> salud, hitboxes
      Interfaces/    -> IDamageable, IGroundChecker, IInputProvider, IPoolable
      Spawning/      -> pooling de objetos, spawners
    Player/
      StateMachine/  -> máquina de estados del jugador (suelo, aire, parkour [deshabilitado], combate)
    Editor/          -> herramientas de Editor (no se compilan en build): SceneAutoLoader,
                        PlayerCharacterSetup
  Characters/
    Player/          -> modelo(s) 3D del personaje (character.fbx)
  Prefabs/           -> Player, Enemy, GameManager, Spawner, etc.
  Scenes/            -> Level-1.unity (la escena del juego)
  LowPoly/           -> modelos y animaciones del personaje/enemigos
  LowPolyCity/        -> asset pack de entorno (incluye su propia escena demo)
  material/          -> materiales sueltos del proyecto
```

## 11. Integrantes

- Historial de commits hasta ahora: ver `git log`. Si eres nuevo en el equipo, agrégate aquí
  con tu rol cuando empieces a aportar.
