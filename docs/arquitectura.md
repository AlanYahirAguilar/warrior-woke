# Awakened Warrior — Arquitectura técnica

> Parte de la documentación del proyecto: [`contexto.md`](contexto.md) (qué es el juego) ·
> **`arquitectura.md`** (cómo está construido) · [`features.md`](features.md) (qué hay
> implementado y buenas prácticas). Los estados (✅ 🟡 🔧 📋 ⬜ ⚠️) se definen en `contexto.md` §1.
>
> Este documento describe el **estado real del código** al 2026-09-29 (commit base `f09a2a3`).
> Lo que todavía no existe aparece marcado como 📋 Planeado o ⬜ Pendiente.

---

## 1. Stack y configuración del proyecto

| Elemento | Valor real | Dónde se ve |
|---|---|---|
| Unity | 6000.6.0f1 | `ProjectSettings/ProjectVersion.txt` |
| Render | URP 17.6.0 (`Universal Render Pipeline Asset.asset`) | `Packages/manifest.json` |
| Input | **Input System 1.20.0**, `activeInputHandler: 1` (solo el nuevo; `UnityEngine.Input` legacy **no** está disponible) | `ProjectSettings/ProjectSettings.asset` |
| Física | 3D (PhysX). Fixed Timestep **0.02 s** (50 Hz). Gravedad −9.81 | `TimeManager.asset`, `DynamicsManager.asset` |
| Navegación | `com.unity.ai.navigation` 2.0.14 instalado, **sin uso todavía** | manifest |
| Otros paquetes relevantes | ProBuilder 6.1.2, Timeline 6.6.0, uGUI 2.6.0, Test Framework 1.8.0, Profile Analyzer 1.4.0 | manifest |
| Paquetes instalados sin uso | Visual Scripting, AI Assistant/Inference, Collab Proxy, Adaptive Performance settings | manifest / `Assets/` |
| Serialización | Force Text | `EditorSettings.asset` |
| Color space | Linear (`m_ActiveColorSpace: 1`) | `ProjectSettings.asset` |
| Lenguaje | C# puro, sin ECS/DOTS ni Visual Scripting | — |
| Namespaces | Solo las herramientas de Editor (`WarriorWoke.EditorTools`). El runtime está en el namespace global | — |
| Assembly definitions | Ninguna (todo compila en `Assembly-CSharp`) | — |
| Tests | Ninguno | — |

### Capas y tags (`ProjectSettings/TagManager.asset`)

| # | Layer | Uso actual |
|---|---|---|
| 6 | `Ground` | Suelo y edificios. Lo leen `GroundChecker` (Player) y `Enemy.groundLayer`. |
| 7 | `Obstacle` | Geometría de parkour. `EnvironmentChecker.obstacleLayer` en el Player. |
| 8 | `Player` | Objetos del Player.prefab. |
| 9 | `Enemy` | Enemy.prefab. `Hitbox.targetLayers` del Player apunta aquí (bits 512). |

Tags: solo los integrados (`Player`, `Untagged`, …). No hay tags personalizados.

## 2. Estructura de carpetas

```
Assets/
  scripts/                    ← todo el código del juego
    Camera/                   CameraFollow.cs
    Core/
      Combat/                 HealthSystem, Hitbox, WeaponData (SO), WeaponHolder, EnemyData (SO)
      Interfaces/             IDamageable, IGroundChecker, IInputProvider, IPoolable
      Spawning/               ObjectPoolManager, Spawner, ReturnToPoolDelay
    Enemy/
      Enemy.cs
      StateMachine/           EnemyState, EnemyStateMachine, States/EnemyGroundedStates.cs
      Types/                  Brute.cs, Looter.cs
    Player/
      Player.cs, PlayerMovement.cs, PlayerInputHandler.cs,
      GroundChecker.cs, EnvironmentChecker.cs
      StateMachine/           PlayerState, PlayerStateMachine,
                              States/{PlayerGroundedStates, PlayerAirStates,
                                      PlayerParkourStates, PlayerCombatStates}.cs
    Editor/                   SceneAutoLoader, PlayerCharacterSetup (no van al build)
  Prefabs/                    Player, Enemy, GameManager, Spawner, Main Camera,
                              Directional Light, Particle System, initial_floor
  Scenes/                     Level-1.unity (+ Level-1/LightingData.asset)
  Characters/Player/          character.fbx (modelo del protagonista)
  LowPoly/                    HumanPlayer.prefab, LowPolyHumanAnimator.controller, Animations/*.fbx
  LowPolyCity/                asset pack de entorno (placeholder) + escena demo
  material/                   ball, enemy, floors, metal (.mat), ZeroFriction.physicMaterial
  InputSystem_Actions.inputactions   ← plantilla por defecto de Unity, SIN USO
  ProBuilder Data/, URPDefaultResources/, Adaptive Performance/
docs/                         contexto.md, arquitectura.md, features.md
GDD_Awakened_Warrior.pdf      GDD final
```

**Convención de carpetas para código nuevo** (📋 propuesta, sigue la estructura existente):

| Tipo de código | Carpeta |
|---|---|
| Sistemas compartidos por jugador y enemigos | `scripts/Core/<Sistema>/` |
| Contratos | `scripts/Core/Interfaces/` |
| Jugador | `scripts/Player/…` |
| Enemigos y jefes | `scripts/Enemy/…` (jefes en `Enemy/Bosses/`) |
| Flujo de juego (checkpoints, guardado, escenas) | `scripts/Game/` (nueva) |
| UI (menús, pausa) | `scripts/UI/` (nueva) |
| Assets de datos (ScriptableObjects) | `Assets/Data/<Tipo>/` (nueva) |
| Audio | `Assets/Audio/` (nueva) |

## 3. Escena y prefabs (estado real)

### `Level-1.unity` (única escena en Build Settings)

Contiene instancias de `GameManager`, `Spawner`, `Main Camera`, `Directional Light` y `Enemy`, el
objeto `Ground` (plano de 100×100 con top en Y = 0, layer Ground, material `floors.mat`), un muro
de ProBuilder (`wall`) y dos casas de LowPolyCity. **El Player no está colocado en la escena:** lo
crea el `Spawner` al iniciar (ver §5.6).

### Prefabs y sus componentes

| Prefab | Componentes de scripts propios | Notas |
|---|---|---|
| `Player` | `Player`, `PlayerMovement`, `PlayerInputHandler`, `GroundChecker`, `EnvironmentChecker`, `HealthSystem`, `Hitbox` | Tag `Player`, layer 8. Rigidbody 70 kg, **Interpolate: None**, rotaciones congeladas. Hijos `CenterPoint`, `HeadPoint`, `Model` (character.fbx). **No tiene** `WeaponHolder` ni `Animator Controller`. |
| `Enemy` | `HealthSystem`, `Hitbox` | ⚠️ **No tiene `Enemy.cs`**, así que la IA no corre. ⚠️ Tag **`Player`** (error: debería ser distinto). `Hitbox.targetLayers = 0` (no puede golpear a nadie). |
| `GameManager` | `ObjectPoolManager` | Pools: `player` → Player.prefab (1), `spawnVFX` → Particle System (1). |
| `Spawner` | `Spawner` | `entityId: player`, `triggerType: OnStart`. |
| `Main Camera` | `CameraFollow` | `autoDetectTarget` activo. |
| `Particle System` | `ReturnToPoolDelay` | VFX de spawn pooleado. |
| `initial_floor`, `Directional Light` | — | `initial_floor` es un blockout viejo y no se usa en `Level-1`. |

## 4. Mapa de dependencias

```mermaid
flowchart LR
    subgraph Player.prefab
      PIH[PlayerInputHandler<br/>IInputProvider] --> P[Player<br/>Facade]
      P --> PM[PlayerMovement<br/>contexto de la FSM]
      PM --> FSM[PlayerStateMachine<br/>+ 12 estados]
      PM --> GC[GroundChecker<br/>IGroundChecker]
      PM --> EC[EnvironmentChecker]
      FSM -. GetComponent en constructor .-> HB[Hitbox]
      FSM -. GetComponent .-> HS[HealthSystem<br/>IDamageable]
      FSM -. GetComponent, opcional .-> WH[WeaponHolder]
    end
    PM -- lee Camera.main.transform --> CAM[CameraFollow]
    CAM -- evento estático Player.OnPlayerSpawned --> P
    SP[Spawner] --> OPM[ObjectPoolManager<br/>singleton]
    OPM -- Instantiate al inicio / SetActive --> Player.prefab
    HB -- TakeDamage --> IDam[(IDamageable)]
    WH --> WD[(WeaponData SO)]
    E[Enemy] --> EFSM[EnemyStateMachine]
    E --> ED[(EnemyData SO)]
    E -- ReturnToPool --> OPM
```

Las líneas punteadas son dependencias que se resuelven con `GetComponent` en el constructor del
estado. Si el componente no está en el prefab, la referencia queda en `null` y el estado usa `?.`
para no fallar; por eso el daño por arma **no se aplica** en la escena actual (`WeaponHolder` no
está en el Player.prefab).

## 5. Sistemas

### 5.1 Jugador: Facade + máquina de estados — ✅ Implementado

```
Player (Facade)                 FixedUpdate → RouteInputToMovement()
 ├─ IInputProvider              PlayerInputHandler (Update: lee y bufferiza)
 └─ PlayerMovement              contexto de la FSM, único dueño del Rigidbody
     ├─ ProcessMovement(...)    guarda inputs, CheckGrounded, UpdateMoveDirection, LogicUpdate
     ├─ FixedUpdate             ApplyFacingRotation + CurrentState.PhysicsUpdate
     └─ PlayerStateMachine      Initialize / ChangeState (Exit → Enter)
```

- **`Player.cs`**: singleton simple (`Player.Instance`) y evento estático
  `OnPlayerSpawned` (se dispara en `OnEnable`). En `FixedUpdate` consume los triggers del input y
  llama a `PlayerMovement.ProcessMovement(...)`. Si faltan `PlayerMovement`, `IInputProvider` o
  `IGroundChecker`, los agrega en `Awake`.
- **`PlayerMovement.cs`**: cachea `Rigidbody`, `CapsuleCollider`, `GroundChecker`,
  `EnvironmentChecker` y `Camera.main.transform` en `Awake`, y construye las 12 instancias de estado.
  Expone el estado de input (`InputX`, `InputZ`, `HasMoveInput`, `JumpTriggered`, …) y helpers de
  física (`SetVelocity`, `SetKinematic`, `ShrinkCollider`, `ResetCollider`, `HasCeilingOverhead`).
  **Regla:** los estados solo mueven el cuerpo a través de estos helpers o de `Rb.MovePosition`.
- **Flujo de un tick:** `LogicUpdate` y `PhysicsUpdate` corren los dos dentro del paso de física
  (el input se enruta desde `Player.FixedUpdate`), así que la lógica de estados corre a 50 Hz y no
  por frame. El input se captura en `Update` y se **bufferiza** con métodos `Consume*()`, para que un
  tap no se pierda entre frames ni se procese dos veces.
- **Movimiento relativo a cámara:** `MoveDirection` es el input proyectado sobre el
  forward/right aplanado de la cámara. La rotación usa `Mathf.SmoothDampAngle` (`turnSmoothTime`) y
  se bloquea en Vault, LedgeGrab, LedgeClimb, WallJump, Block y los ataques.
- **Transiciones:** cada estado decide sus salidas en `LogicUpdate`. `PlayerStateMachine` es
  genérica y no conoce estados concretos. Un estado nuevo se agrega creando la clase, instanciándola
  en `PlayerMovement.BuildStateMachine()` y agregando sus transiciones en los estados de origen.
- **Convención de pivote:** el origen del Player es el **centro del torso**
  (`CapsuleCollider.center = (0,0,0)`), `CenterPoint` está en el origen y `HeadPoint` en `height/2`.
  `GroundChecker` calcula el pie con `capsule.bounds.min.y`. Si cambias el modelo, usa
  **Tools → Warrior Woke → Configurar Modelo del Jugador** (respeta la convención).

**Estados** (archivos en `Player/StateMachine/States/`):

| Archivo | Estados |
|---|---|
| `PlayerGroundedStates.cs` | `Idle`, `Run` (auto-sprint), `Slide` |
| `PlayerAirStates.cs` | `Jump`, `WallJump` (⏸️ desactivado: la clase existe pero ninguna transición llega a ella) |
| `PlayerParkourStates.cs` | `Vault`, `LedgeGrab`, `LedgeClimb` |
| `PlayerCombatStates.cs` | `LightAttack`, `HeavyAttack`, `Block`, `Dodge` |

```mermaid
stateDiagram-v2
    Idle --> Run: input
    Idle --> Jump: Espacio + suelo
    Idle --> Block: F
    Idle --> Dodge: E + suelo
    Idle --> LightAttack: clic izq
    Idle --> HeavyAttack: clic der
    Run --> Vault: obstáculo bajo (automático)
    Run --> Slide: Shift
    Run --> Jump
    Run --> Idle: sin input
    Jump --> LedgeGrab: cornisa detectada
    Jump --> Idle: aterriza
    LedgeGrab --> LedgeClimb: Espacio
    LedgeGrab --> Idle: dirección opuesta
    LedgeClimb --> Idle
    Vault --> Run
    Slide --> Run: 0.7 s sin techo
    LightAttack --> LightAttack: clic izq en ventana (máx 3)
    LightAttack --> HeavyAttack: clic der en ventana
    LightAttack --> Idle
    HeavyAttack --> Idle: 0.8 s
    Block --> Idle: suelta F
    Block --> LightAttack: contraataque
    Dodge --> Run: 0.5 s
    Dodge --> Idle: 0.5 s
```

> **Wall jump desactivado (2026-09-29, decisión del equipo):** la transición `Jump → WallJump`
> está comentada en `PlayerJumpState.LogicUpdate`. `PlayerWallJumpState`, `ConsecutiveWallJumps` y
> `EnvironmentChecker.IsTouchingWall` se conservan para poder reactivarlo.
>
> **Nota:** `Dodge` usa un cooldown `static` (`_lastDodgeTime`), pero ninguna transición consulta
> `PlayerDodgeState.CanDodge`, así que **hoy el cooldown no se aplica**. Ver `features.md`.

### 5.2 Input — ✅ Implementado (modo prototipo)

`PlayerInputHandler : IInputProvider`. Cada acción tiene dos caminos: un `InputActionReference`
opcional (todos vacíos en el prefab) o la lectura directa de `Keyboard.current`/`Mouse.current`.
**Hoy se usa la lectura directa.** La documentación oficial del Input System la describe como
adecuada solo para prototipos, porque se salta el rebinding, los esquemas de control y el gamepad.
`Assets/InputSystem_Actions.inputactions` es la plantilla por defecto de Unity y no está conectada.

### 5.3 Detección de entorno — ✅ Implementado

- **`GroundChecker : IGroundChecker`**: un `Physics.Raycast` hacia abajo desde
  `bounds.min.y + 0.1` con longitud `0.1 + extraDistance` contra `groundLayer` (Ground + Obstacle).
- **`EnvironmentChecker`**: pre-filtro `Physics.OverlapSphereNonAlloc` (buffer de 4, radio 1.2)
  antes de los raycasts. Pared = raycast desde `CenterPoint`. Vault = golpea el centro y no la
  cabeza. Cornisa = golpean centro y cabeza, más un raycast hacia abajo desde arriba y adelante para
  encontrar la esquina. Todo usa `transform.forward` como dirección.

### 5.4 Combate — 🟡 Parcial

| Script | Responsabilidad |
|---|---|
| `HealthSystem : IDamageable` | Vida con clamp, i-frames por tiempo (`iFramesDuration` 0.5 s), `ActivateIFrames(d)`, `Heal`, `InstantKill`, `InitializeHealth(max)`. Eventos `OnHealthChanged`, `OnDeath`, `OnDamageReceived`. Sin `Update`. |
| `Hitbox` | `Activate()` hace un `OverlapSphereNonAlloc` (buffer de 10) contra `targetLayers` y llama `TakeDamage` en cada `IDamageable`. `SetDamage`, `SetRadius`, evento `OnHit`. Es un pulso instantáneo, no un trigger persistente. |
| `WeaponData` (SO) | Nombre, icono, daño ligero/pesado, radio de hitbox, knockback, `weaponId`. Menú `Create > WarriorWoke > Weapon Data`. **No hay assets creados.** |
| `WeaponHolder` | Arma equipada, `Equip(data)`, `GetLightDamage()` (8 desarmado), `GetHeavyDamage()` (20), `GetKnockback()`, evento `OnWeaponChanged`. **No está en el Player.prefab.** |

Limitaciones reales: el bloqueo no reduce daño (ningún sistema lee `DamageReductionMultiplier`),
`Hitbox` no verifica dirección (frontal) ni evita pegarle dos veces al mismo objetivo si tiene
varios colliders, el knockback no se aplica y no hay animaciones.

### 5.5 Enemigos — 🟡 Parcial / ⚠️ desalineado con el GDD

- **`Enemy : MonoBehaviour, IPoolable`**: el mismo patrón de contexto + FSM que el jugador. Lee sus
  valores de un `EnemyData` (SO). Estados en `EnemyGroundedStates.cs`: `Patrol`, `Chase`, `Attack`,
  `Dead`. La detección es un `OverlapSphereNonAlloc` + `CompareTag("Player")` + raycast de línea de
  visión. Al morir espera 1.5 s y vuelve al pool.
- ⚠️ **Es 2.5D:** congela Z, se mueve solo en X (`Move(float dir)`) y rota a ±90°. El jugador ya es
  3D libre, así que los enemigos **no pueden perseguirlo en profundidad**.
- ⚠️ `Looter`/`Brute` vienen del GDD anterior. `Brute` declara `blockProbability`, pero no hay
  estado de bloqueo (el comentario describe un `BruteBlockState` que no existe).
- ⚠️ `CheckGrounded()` llama a `GetComponent<CapsuleCollider>()` en cada `FixedUpdate` (viola la
  regla de cacheo).
- ⚠️ `Enemy.prefab` no usa este script (ver §3).

### 5.6 Spawning y object pooling — ✅ Implementado

- **`ObjectPoolManager`** (singleton en `GameManager`): pre-instancia `initialSize` copias por
  `poolId` en `Awake`, `Spawn(id, pos, rot)` las saca de una `Queue` (y crece si se vacía) y
  `ReturnToPool(obj, id)` las regresa. Llama `IPoolable.OnSpawn/OnDespawn`.
- **`Spawner`**: pide un `entityId` al pool según `OnStart` / `Timer` / `OnTriggerEnter` /
  `Manual`, y opcionalmente un `spawnEffectId`.
- **`ReturnToPoolDelay : IPoolable`**: devuelve el objeto al pool tras `delay` (usa un
  `WaitForSeconds` cacheado).
- **El jugador nace del pool:** `Spawner (OnStart) → ObjectPoolManager.Spawn("player")`. Para
  cambiar dónde aparece, mueve el `Spawner`.
- Nota: el pool es propio. Unity 6 trae `UnityEngine.Pool.ObjectPool<T>`, con `collectionCheck`
  y `maxSize`. No hace falta migrar mientras el pool actual funcione.

### 5.7 Cámara — 🟡 Parcial

`CameraFollow` (en `Main Camera`, `LateUpdate`): se coloca en
`target + up·shoulderHeight + right·shoulderSide − forward·distance`, hace `SmoothDamp` de la
posición y `Slerp` de la rotación, y usa un `SphereCast` para no atravesar paredes. Encuentra al
jugador con `Player.OnPlayerSpawned`, luego `Player.Instance`, luego `FindAnyObjectByType` y al
final el tag.
**Falta lo del GDD:** control libre con el ratón (hoy la cámara solo sigue el `forward` del
jugador), camera shake, encuadre de combate y ajuste de zoom.

### 5.8 Progresión — sin XP (decisión cerrada)

El juego **no tiene experiencia ni niveles de personaje** (GDD §7, §19). `PlayerXpSystem`,
`IXpReceiver` y `EnemyData.xpReward` se eliminaron el 2026-09-29. No se deben reintroducir: la
progresión es solo por historia (niveles y mundos).

### 5.9 Herramientas de Editor — ✅ Implementado

- `SceneAutoLoader` (`[InitializeOnLoad]` + `SessionState`): abre `Level-1.unity` una vez por sesión.
- `PlayerCharacterSetup` (menú **Tools → Warrior Woke → Configurar Modelo del Jugador**): configura
  `character.fbx` como Humanoid (o Generic), lo pone como hijo `Model` del Player.prefab, lo
  reescala si hace falta y ajusta `CapsuleCollider` y `HeadPoint`. Es idempotente.

### 5.10 Animación — ⬜ Pendiente

Existen `Assets/LowPoly/LowPolyHumanAnimator.controller` (1 estado: `Idle`) y clips `LowPoly`
(Idle, Run*, Sprint, Jumps, Roll*, Punch*, MeleeAttack*, Blocking, GetHit, Death, BowShot). Nada
está conectado al Player. Los estados tienen `// TODO: trigger animation`.

### 5.11 Sistemas que no existen todavía — ⬜ Pendiente

Checkpoints, muerte/reaparición, caída mortal, regeneración de vida, recoger armas, IA del arquero
(ataque a distancia), jefes, zonas de enemigo, guardado, menú principal, pausa, flujo de escenas y
niveles, audio, feedback de daño (VFX, camera shake, estado visual de salud) y build de Windows.

## 6. Decisiones técnicas vigentes

| # | Decisión | Motivo |
|---|---|---|
| D1 | Jugador con **Rigidbody dinámico** + velocidad escrita por los estados (no `CharacterController`) | Ya implementado y funcionando; permite `MovePosition` kinemático en parkour. |
| D2 | **FSM por clases** (una clase por estado, transiciones dentro del estado) para jugador y enemigos | Coincide con lo que pide el GDD para la IA (§21) y mantiene los estados testeables y aislados. |
| D3 | **Facade** (`Player`) + **contexto** (`PlayerMovement`) | Separa el enrutado de input de la física y la lógica. |
| D4 | **Interfaces** para desacoplar (`IDamageable`, `IInputProvider`, `IGroundChecker`, `IPoolable`) | `Hitbox` no conoce al jugador ni al enemigo; el input se puede cambiar sin tocar el movimiento. |
| D5 | **ScriptableObjects** para datos de diseño (`WeaponData`, `EnemyData`) | Los datos se comparten entre instancias y se ajustan sin tocar código. Son de **solo lectura en runtime** en builds. |
| D6 | **Object pooling** para todo lo que nace y muere seguido | Evita GC e `Instantiate` en el game loop. |
| D7 | **Eventos C# (`System.Action`)** para notificar (`OnDeath`, `OnHealthChanged`, `OnPlayerSpawned`) | La UI, el audio y la cámara se enganchan sin acoplarse. Se suscriben en `OnEnable` y se desuscriben en `OnDisable`. |
| D8 | Movimiento **relativo a cámara** y cámara que sigue el `forward` del jugador | Controlador estándar de tercera persona. |
| D9 | Cero allocations en código caliente (`NonAlloc`, buffers prealocados, sin LINQ ni strings en loops) | Ver buenas prácticas en `features.md`. |

## 7. Plan técnico para lo que falta — 📋 Propuesta

> Nada de esta sección está implementado. Las marcadas con **❓** siguen pendientes de aprobación
> (ver §8). Las marcadas con **✔** ya están aprobadas. Cuando algo se implemente, pasa a §5 y a `features.md`.

| Sistema | Diseño propuesto | Encaja con |
|---|---|---|
| **Input** ❓ | Crear `Assets/Input/AwakenedWarrior.inputactions` con el mapa `Gameplay` (Move, Look, Sprint, Jump, LightAttack, BlockHold, Dodge, Interact, Pause) usando los bindings del GDD §14 + gamepad, y asignar las referencias en `PlayerInputHandler`. El código **ya soporta** `InputActionReference`, así que no hace falta reescribirlo. | Input System, doc oficial ("Using Actions" es el flujo recomendado). |
| **Controles** ✔ | Adoptar los del GDD §14: J ligero, K fuerte, L bloqueo (mantener), Q esquiva + dirección, E recoger, Shift sprint (mantener), Espacio salto/vault, ESC pausa. El slide pierde Shift y necesita una tecla nueva (por definir). | GDD §14, decisión P1. |
| **Sprint** ✔ | Reemplazar el auto-sprint por "mantener Shift" (`IInputProvider.IsSprintHeld`), con `SprintSpeed = BaseSpeed × 1.4`. Se cancela al recibir daño, bloquear o atacar. | GDD §5.2. |
| **Vault** ✔ | Solo con Espacio + `IsObstacleVaultable` (hoy es automático al correr contra un obstáculo). | GDD §5.4. |
| **Bloqueo** | `HealthSystem` recibe un modificador de daño (p. ej. una interfaz `IDamageModifier` que implementa el estado de bloqueo, o un `float DamageMultiplier` que pone el estado). Reducción del 70 % solo si `Vector3.Dot(forward, dirHaciaAtacante) > umbral`. | GDD §5.8. |
| **Regeneración de vida** | Componente `HealthRegen` que escucha `OnDamageReceived` y, tras N segundos sin daño, llama a `Heal` por tick (con timer, sin corrutina por frame). | GDD §5.11, D7. |
| **Caída mortal** | En `PlayerMovement`/`GroundChecker`, registrar la altura al despegar y llamar `InstantKill()` al aterrizar si la caída supera X m. Los barrancos pueden usar un trigger `KillZone`. | GDD §5.11, §5.12. |
| **Checkpoints / respawn** | `Checkpoint` (trigger, una activación) → `CheckpointManager` (en la escena) guarda la posición. Al `OnDeath` del Player: reposicionar con `Rigidbody.position` (teleport), `HealthSystem.InitializeHealth(100)` y reiniciar la FSM en `Idle`. | GDD §5.13, §17. |
| **Guardado** | `SaveSystem` estático: `JsonUtility` → `Application.persistentDataPath/save.json` con `{nivel, checkpointId}`. Una sola partida. | GDD §26, doc oficial de JsonUtility. |
| **Armas** | Agregar `WeaponHolder` al Player.prefab. Crear 3 `WeaponData` (katana 20/35, yari 18/30 con más radio, kanabo 30/50) y un prefab `WeaponPickup` (trigger + E). Al recoger, se suelta la actual como pickup. Desarmado 10/20. | GDD §5.10, §18, D5. |
| **Enemigos** ✔ | Reescribir `Enemy` para 3D: **NavMeshAgent** (paquete ya instalado) con **Rigidbody kinemático**, como recomienda la documentación de AI Navigation. `NavMeshSurface` en cada nivel. Estados del GDD §21 (Idle, Detectar, Acercarse, Atacar, Defenderse, Buscar, Regresar) y una zona asignada (`EnemyZone`) de la que no salen. Tipos por `EnemyData`: `Archer` (distancia + flecha pooleada), `LightWarrior`, `HeavyWarrior`. La apariencia (modelos) la entrega el equipo en un paquete aparte. | GDD §5.14, §12, §21, D2, D5, D6. |
| **Jefes** | `Boss : Enemy` con estados extra (Analizar distancia, Reposicionarse, Bloquear, Esquivar), sin fases. `BossArena` cierra la salida con un trigger. | GDD §5.15, §13. |
| **Cámara** ✔ | Extender `CameraFollow` (sin Cinemachine) con yaw/pitch del ratón (`Look`), límites de pitch, cursor bloqueado en gameplay, shake por evento y encuadre de combate. El movimiento sigue siendo relativo a la cámara, pero la cámara deja de depender del `forward` del jugador. | GDD §15. |
| **Animación** | Un `Animator Controller` del jugador con parámetros hasheados (`Animator.StringToHash`) en un componente `PlayerAnimator` que escucha los cambios de estado. Los estados no llaman al Animator directamente. | GDD pilar 2. |
| **Flujo de juego** | Escenas `MainMenu`, `World1_Level1`, `World1_Level2`, `World2_Level3`, cargadas con `SceneManager.LoadSceneAsync`. Pausa con `Time.timeScale = 0` y un panel de UI (uGUI). | GDD §17. |
| **Audio** | `AudioManager` con fuentes de audio pooleadas para SFX, que se suscribe a los eventos de combate. | GDD §23, D6, D7. |

## 8. Decisiones de alcance

| # | Tema | Decisión | Fecha | Estado |
|---|---|---|---|---|
| P1 | Controles | Adoptar los del GDD §14 (J/K/L/Q/E, Shift = sprint mantenido, Espacio = salto/vault). | 2026-09-29 | ✔ Aprobada, 📋 por implementar. **Abierto:** tecla nueva del slide. |
| P2 | Parkour fuera del GDD | **Solo se desactiva el wall jump.** Ledge grab/climb y slide se conservan activos. | 2026-09-29 | ✅ Implementada |
| P3 | Sistema de XP | Eliminarlo: el juego no tiene XP. | 2026-09-29 | ✅ Implementada |
| P4 | Enemigos | Reescribir en 3D con NavMeshAgent. El equipo entrega el paquete de apariencia. | 2026-09-29 | ✔ Aprobada, 📋 por implementar |
| P5 | Cámara | Extender `CameraFollow` con control de ratón (sin Cinemachine). | 2026-09-29 | ✔ Aprobada, 📋 por implementar |
| P6 | Input | Migrar a un asset `.inputactions` propio. | — | ❓ Pendiente: el equipo pidió primero una explicación |

## 9. Deuda técnica y bugs conocidos

| # | Problema | Dónde | Impacto |
|---|---|---|---|
| T1 | `Enemy.prefab` tiene tag `Player` | `Prefabs/Enemy.prefab` | El fallback de `CameraFollow` y los triggers por tag pueden confundir al enemigo con el jugador. |
| T2 | `Enemy.prefab` no tiene `Enemy.cs`, y `Hitbox.targetLayers = 0` | `Prefabs/Enemy.prefab` | El enemigo de la escena no tiene IA ni puede hacer daño. |
| T3 | Enemigos 2.5D (freeze Z, eje X) | `Enemy.cs`, `EnemyGroundedStates.cs` | Incompatible con el jugador 3D. |
| T4 | El bloqueo no reduce daño | `PlayerBlockState`, `HealthSystem` | El bloqueo solo inmoviliza al jugador. |
| T5 | El cooldown de esquiva no se aplica | `PlayerDodgeState.CanDodge` sin uso | Se puede esquivar sin límite. |
| T6 | `WeaponHolder` no está en el Player.prefab | `Prefabs/Player.prefab` | El daño usa el valor fijo del `Hitbox` y no el de `WeaponHolder`. |
| T7 | Rigidbody sin interpolación mientras la cámara sigue en `LateUpdate` | Player.prefab, `CameraFollow` | Posible jitter visual. Según la documentación oficial, la interpolación se activa solo si se observa jitter. Hay que verificarlo en Play. |
| T8 | `GetComponent` por tick | `Enemy.CheckGrounded()` | Viola la regla de cacheo. |
| T9 | Comentarios que citan otro GDD (`GDD §8`, `§14`, "Ashfall", "~70% per GDD" con 95 % en el código) | varios scripts | Pueden confundir. Hay que corregirlos al tocar cada archivo. |
| T10 | `SetVelocity(float x, float y)` pone Z en 0 | `PlayerMovement` | Correcto hoy (solo se usa con `x = 0` para frenar), pero es una trampa si se usa con x ≠ 0. |
| T11 | `EnvironmentChecker.OnDrawGizmosSelected` dibuja hacia `transform.right` | `EnvironmentChecker.cs` | Los gizmos no coinciden con los raycasts reales (`forward`). Solo afecta a la depuración. |
| T12 | `InputSystem_Actions.inputactions` sin uso | `Assets/` | Puede confundir; se reemplaza en P6. |
