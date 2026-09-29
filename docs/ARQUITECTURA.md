# Arquitectura técnica

Cómo está armado el código, no qué hace el juego (para eso ver `CONTEXTO_JUEGO.md`). Esto es una
foto de la arquitectura tal como quedó tras la sesión de setup + cámara al hombro — revisa
`git log` para lo que cambie después.

## Stack

- **Unity 6000.6.0f1**, **URP** (Universal Render Pipeline).
- **New Input System** (`UnityEngine.InputSystem`) — con fallback manual a `Keyboard`/`Mouse`
  cuando no hay `InputActionReference` asignado (ver sección Input).
- C# puro para toda la lógica de gameplay (sin ECS/DOTS, sin Visual Scripting pese a que el
  paquete está instalado).

## Principios que el código ya sigue (y por qué importan al tocarlo)

- **SRP por componente**: cada `MonoBehaviour` hace una cosa (`GroundChecker` solo detecta piso,
  `EnvironmentChecker` solo detecta paredes/cornisas, `HealthSystem` solo vida). Si vas a agregar
  lógica, prefiere un componente nuevo a inflar uno existente.
- **Interfaces para desacoplar** (`Assets/scripts/Core/Interfaces/`): `IInputProvider`,
  `IGroundChecker`, `IDamageable`, `IPoolable`. El resto del código depende de estas interfaces,
  no de las clases concretas — así `PlayerMovement` no le importa si el input viene del teclado o
  de un `InputActionReference`, y `ObjectPoolManager` no le importa qué tipo de objeto está
  poolando.
- **Cero allocations por frame** en los loops calientes (`Update`/`FixedUpdate`): no hay `new()`
  ni closures dentro de esos métodos en el código de Player/Combat/Spawning. Si agregas algo ahí,
  mantenlo así (cachea listas/`WaitForSeconds`/etc. fuera del loop).

## Jugador: Facade + State Machine

```
Player.cs (Facade)
 ├─ PlayerInputHandler   (IInputProvider) — lee teclado/mouse/Input System
 ├─ PlayerMovement                        — dueño de la StateMachine y la física
 │   └─ PlayerStateMachine
 │       ├─ PlayerIdleState
 │       ├─ PlayerRunState
 │       ├─ PlayerJumpState
 │       ├─ PlayerSlideState
 │       ├─ PlayerLightAttackState / PlayerHeavyAttackState
 │       ├─ PlayerBlockState / PlayerDodgeState
 │       └─ [deshabilitados] PlayerVaultState, PlayerLedgeGrabState,
 │                            PlayerLedgeClimbState, PlayerWallJumpState
 ├─ GroundChecker         (IGroundChecker) — raycast contra CapsuleCollider.bounds
 ├─ EnvironmentChecker                     — raycasts de pared/cornisa (solo usado por
 │                                           los estados deshabilitados de arriba)
 └─ HealthSystem / Hitbox                  — combate
```

- **`Player.cs`** es un Facade: no implementa lógica de movimiento ni de input, solo orquesta —
  cachea referencias en `Awake()` y en `FixedUpdate()` lee del `IInputProvider` y se lo pasa a
  `PlayerMovement.ProcessMovement(...)`. También expone `Player.Instance` (singleton simple) y el
  evento estático `OnPlayerSpawned`, que usa `CameraFollow` para auto-detectar al jugador sin que
  nadie tenga que arrastrar una referencia en el Inspector.
- **`PlayerMovement`** es el dueño real del estado: cachea `Rigidbody`/`CapsuleCollider`, construye
  la `PlayerStateMachine` con todas las instancias de estado en `Awake()`, y expone propiedades de
  solo lectura (`InputX`, `InputZ`, `MoveDirection`, `IsGrounded`, etc.) que los estados leen. Los
  estados nunca tocan el Rigidbody directamente — todo pasa por `SetVelocity(...)`,
  `SetKinematic(...)`, `ShrinkCollider(...)` para que `PlayerMovement` sea el único punto que sabe
  cómo se mueve físicamente el personaje.
- **Movimiento (ver también `docs/CAMBIOS.md`)**: el input crudo (`InputX`/`InputZ`, -1..1 cada
  uno) se proyecta sobre el forward/right "aplanado" (sin componente Y) de `Camera.main` para
  obtener `MoveDirection`, un vector de mundo normalizado. El personaje gira hacia esa dirección
  con `Mathf.SmoothDampAngle` (suaviza el giro; ver `turnSmoothTime` en el Inspector). Esto hace
  que **cámara y movimiento estén acoplados por diseño**: la cámara sigue `transform.forward` del
  jugador, y el jugador interpreta el input según hacia dónde mira la cámara — es el patrón
  estándar de un controlador de tercera persona (el mismo que usan los tutoriales clásicos de
  Brackeys/Unity para este tipo de cámara).
- **Convención de pivote**: el `Transform` raíz del Player representa el **centro del torso**, no
  los pies (`CapsuleCollider.center = (0,0,0)`, con `CenterPoint` en el mismo punto para los
  raycasts de `EnvironmentChecker`, y `HeadPoint` en `height/2` hacia arriba). Si alguna vez
  cambias el modelo o el collider, respeta esta convención o vas a tener que tocar
  `GroundChecker`/`EnvironmentChecker` también.
- **Patrón State**: cada estado (`PlayerState` abstracta: `Enter/LogicUpdate/PhysicsUpdate/Exit`)
  decide sus propias transiciones llamando a `stateMachine.ChangeState(...)`. No hay una tabla de
  transiciones centralizada — las reglas de "de qué estado a cuál" viven dentro de cada estado. Al
  agregar un estado nuevo, decide sus transiciones ahí, no en `PlayerStateMachine` (que es
  deliberadamente genérica y no sabe nada de estados concretos).

### Input

`PlayerInputHandler` implementa `IInputProvider`. Cada acción tiene dos caminos: si hay un
`InputActionReference` asignado en el Inspector, lo usa; si no, cae a leer `Keyboard`/`Mouse`
directamente (`Keyboard.current`, WASD/flechas + Space/Shift/F/E + click izq/der). Ahora mismo el
proyecto usa el camino de fallback (los `InputActionReference` están vacíos en el prefab) — no es
un bug, es la configuración actual. Si en algún momento se migra a Input Actions "de verdad"
(un asset `.inputactions` con un mapa de acciones), solo hay que asignar las referencias; el
código ya soporta ambos caminos sin cambios.

### Combate

- **`HealthSystem`** (`IDamageable`): vida con clamp, i-frames por tiempo (no coroutine),
  eventos (`OnHealthChanged`, `OnDeath`, `OnDamageReceived`) para que UI/VFX se enganchen sin
  acoplarse al combate.
- **`Hitbox`**: overlap por radio contra `targetLayers`, aplica daño a lo que implemente
  `IDamageable`. Los estados de ataque (`PlayerLightAttackState`/`PlayerHeavyAttackState`) todavía
  no disparan animación real (hay comentarios `// TODO: trigger animation` marcando dónde
  conectar un `Animator` cuando exista uno).

## Spawning y Object Pooling

```
GameManager (ObjectPoolManager)
  pools: [ "player" -> Player.prefab, "spawnVFX" -> partícula ]

Spawner (uno por punto de spawn en la escena)
  entityId: "player"  |  triggerType: OnStart / Timer / OnTriggerEnter / Manual
  → ObjectPoolManager.Instance.Spawn(entityId, transform.position, transform.rotation)
```

- **`ObjectPoolManager`** es un singleton (`Instance`) que pre-instancia N copias de cada prefab
  configurado (`pools`), desactivadas, y las entrega/recicla por `poolId` (string). Los objetos
  poolables implementan `IPoolable` (`PoolId`, `OnSpawn()`, `OnDespawn()`) para resetear su propio
  estado al reciclarse — el pool nunca sabe qué tipo de objeto es, solo lo activa/desactiva y
  reposiciona.
- **`Spawner`** es deliberadamente "tonto": no sabe pooling, solo le pide al
  `ObjectPoolManager` que spawnee un `entityId` en su propia posición, según un trigger
  configurable (`OnStart`, `Timer`, `OnTriggerEnter`, `Manual`).
- **El jugador se spawnea así, no está colocado a mano en la escena.** El `Spawner` de
  `Level-1.unity` tiene `entityId: player`, `triggerType: OnStart` — el Player.prefab pooleado en
  `GameManager` aparece en la posición del Spawner apenas arranca la escena. Si quieres mover
  dónde aparece el jugador, mueve el `Spawner`, no busques un Player en la Hierarchy antes de que
  el juego corra (no existe hasta el primer `Spawn()`).

## Cámara

`CameraFollow` (en Main Camera) es standalone respecto al jugador: se engancha al evento estático
`Player.OnPlayerSpawned` para encontrar su target sin referencias manuales, y en `LateUpdate`
calcula una posición "al hombro" (`shoulderHeight`/`shoulderSide`/`distance` desde el target) con
un `SphereCast` de por medio para no atravesar paredes. No conoce `PlayerMovement` ni al revés —
la única relación entre ambos es indirecta: el jugador lee `Camera.main.transform` para su
movimiento relativo a cámara, y la cámara lee `transform.forward` del jugador para su encuadre.

## Herramientas de Editor (`Assets/scripts/Editor/`, no se compilan en build)

- **`SceneAutoLoader`**: `[InitializeOnLoad]` + `SessionState` — carga `Level-1.unity` una vez por
  sesión de Editor si no es la escena activa.
- **`PlayerCharacterSetup`**: `[MenuItem]` — reemplaza el modelo visual de `Player.prefab` y ajusta
  collider/`HeadPoint` a las dimensiones reales del modelo importado. Ver `docs/CAMBIOS.md` para
  el detalle de por qué esto tiene que correr dentro del Editor y no se puede hacer editando YAML
  a mano.

## Dónde vive cada cosa

```
Assets/
  scripts/
    Camera/CameraFollow.cs
    Core/
      Combat/{HealthSystem,Hitbox}.cs
      Interfaces/{IDamageable,IGroundChecker,IInputProvider,IPoolable}.cs
      Spawning/{ObjectPoolManager,Spawner,ReturnToPoolDelay}.cs
    Player/
      Player.cs, PlayerMovement.cs, PlayerInputHandler.cs,
      GroundChecker.cs, EnvironmentChecker.cs
      StateMachine/
        PlayerState.cs, PlayerStateMachine.cs
        States/{PlayerGroundedStates,PlayerAirStates,PlayerParkourStates,PlayerCombatStates}.cs
    Editor/{SceneAutoLoader,PlayerCharacterSetup}.cs
  Prefabs/{Player,Enemy,GameManager,Spawner,Main Camera,...}.prefab
  Characters/Player/character.fbx
  Scenes/Level-1.unity
```

## Deuda técnica conocida (documentada a propósito, no escondida)

- **Parkour deshabilitado**: `PlayerVaultState`, `PlayerLedgeGrabState`, `PlayerLedgeClimbState`,
  `PlayerWallJumpState` y buena parte de `EnvironmentChecker` siguen escritos pero inalcanzables
  desde que el movimiento pasó a 3D libre (asumen raycasts en un eje mundial fijo). Rehacerlos
  implica decidir cómo se detectan paredes/cornisas en un mundo abierto, no es un simple ajuste.
- **Sin animaciones conectadas todavía**: los estados de combate tienen comentarios
  `// TODO: trigger animation` señalando dónde falta un `Animator Controller` una vez que el
  modelo importado traiga (o se le agreguen) clips.
- **`FacingDirection` (escalar ±1)** se mantiene solo por compatibilidad con los estados de
  parkour deshabilitados — el movimiento real ya no lo usa, usa `MoveDirection`
  (`Vector3`) y `transform.forward`. Si algún día se borra el código de parkour, este campo
  también se puede borrar.
