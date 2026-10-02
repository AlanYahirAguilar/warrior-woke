# Awakened Warrior — Arquitectura técnica

> Parte de la documentación del proyecto: [`contexto.md`](contexto.md) (qué es el juego) ·
> **`arquitectura.md`** (cómo está construido) · [`features.md`](features.md) (qué hay
> implementado y buenas prácticas). Los estados (✅ 🟡 🔧 📋 ⬜ ⚠️ ⏸️) y las marcas ✔/❓ se
> definen en `contexto.md` §1.
>
> Este documento describe el **estado real del código** al 2026-10-01 (pasada de contacto físico del
> parkour y Parkour Test Area; ver el historial en `features.md` §5). Lo que todavía no existe
> aparece marcado como 📋 Planeado o ⬜ Pendiente.

---

## 1. Stack y configuración del proyecto

| Elemento | Valor real | Dónde se ve |
|---|---|---|
| Unity | 6000.6.0f1 | `ProjectSettings/ProjectVersion.txt` |
| Render | URP 17.6.0 (`Universal Render Pipeline Asset.asset`) | `Packages/manifest.json` |
| Input | **Input System 1.20.0**, `activeInputHandler: 1` (solo el nuevo; `UnityEngine.Input` legacy **no** está disponible). Sin asset `.inputactions` ni project-wide actions registradas | `ProjectSettings/ProjectSettings.asset`, `EditorBuildSettings.asset` |
| Identidad del build | `productName: Awakened Warrior`, `companyName: SUNUX GAMES`, `com.SUNUXGAMES.AwakenedWarrior` | `ProjectSettings/ProjectSettings.asset` |
| Física | 3D (PhysX). Fixed Timestep **0.02 s** (50 Hz). Gravedad −9.81 | `TimeManager.asset`, `DynamicsManager.asset` |
| Navegación | `com.unity.ai.navigation` 2.0.14 instalado, **sin uso todavía** | manifest |
| Otros paquetes relevantes | ProBuilder 6.1.2, Timeline 6.6.0, uGUI 2.6.0, Test Framework 1.8.0, Profile Analyzer 1.4.0 | manifest |
| Paquetes instalados sin uso | Visual Scripting, AI Assistant (preview) / Inference, Collab Proxy, Device Simulator Devices, `com.unity.pipeline` 0.6.0-exp.1 (experimental), uGUI, Adaptive Performance settings | manifest / `Assets/` |
| Serialización | Force Text | `EditorSettings.asset` |
| Color space | Linear (`m_ActiveColorSpace: 1`) | `ProjectSettings.asset` |
| Lenguaje | C# puro, sin ECS/DOTS ni Visual Scripting | — |
| Namespaces | Solo las herramientas de Editor (`WarriorWoke.EditorTools`). El runtime está en el namespace global | — |
| Assembly definitions | Ninguna (todo compila en `Assembly-CSharp`) | — |
| Tests | Sin assembly de Test Framework. Pruebas propias de Editor: validación en batch y recorrido en Play Mode real (§5.9) | `scripts/Editor/` |

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
      Interfaces/             IDamageable, IDamageModifier, IGroundChecker, IInputProvider, IPoolable
      Spawning/               ObjectPoolManager, Spawner, ReturnToPoolDelay
    Enemy/
      Enemy.cs
      StateMachine/           EnemyState, EnemyStateMachine, States/EnemyGroundedStates.cs
    Player/
      Player.cs, PlayerMovement.cs, PlayerInputHandler.cs,
      PlayerAnimator.cs, PlayerAnimatorIds.cs, PlayerAnimatorIK.cs, PlayerContactIK.cs,
      IParkourAnimationProgress.cs, ParkourTimings.cs, VaultInfo.cs, LedgeInfo.cs,
      GroundChecker.cs, EnvironmentChecker.cs
      StateMachine/           PlayerState, PlayerStateMachine,
                              States/{PlayerGroundedStates, PlayerAirStates,
                                      PlayerParkourStates, PlayerCombatStates}.cs
    Editor/                   SceneAutoLoader, PlayerCharacterSetup, PlayerAnimationSetup,
                              ParkourTestCircuitBuilder, ParkourPlayModeTest (no van al build)
  Prefabs/                    Player, Enemy, GameManager, Spawner, Main Camera,
                              Directional Light, Particle System
  Scenes/                     Level-1.unity (la iluminación horneada Scenes/<Escena>/ no se versiona)
  Characters/Player/          character.fbx (modelo Ch45 de Mixamo, Humanoid),
                              PlayerAnimator.controller (generado),
                              Textures/ (5 texturas extraídas de character.fbx),
                              Animations/ (2 transiciones de guardia Ch45)
  LowPoly/                    Animations/*.fbx (clips Humanoid usados por el jugador; paquete
                              "FREE Low Poly Human - RPG Character" de la Asset Store)
  ThirdParty/DynamicParkourSystem/
                              Animations/ (11 clips Mixamo del Dynamic Parkour System),
                              LICENSE.txt (MIT), README.md (qué se tomó y cómo se adaptó)
  Tests/ParkourCircuit/       Materials/ del Parkour Test Area (un color por sección)
  LowPolyCity/                asset pack de entorno (placeholder) + escena demo
  material/                   enemy, floors (.mat), ZeroFriction.physicMaterial
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

### `Level-1.unity` (única escena en Build Settings): Parkour Test Area

Desde el 2026-10-01 (decisión P24) la escena es el área de pruebas del parkour. La construye
`ParkourTestCircuitBuilder` (**Tools → Warrior Woke → Construir Parkour Test Area**) y contiene:
instancias de `GameManager`, `Spawner`, `Main Camera` y `Directional Light`, y el objeto
`ParkourTestArea` (suelo, perímetro y siete secciones). **El Player no está colocado en la escena:**
lo crea el `Spawner` al iniciar (ver §5.6). La escena no referencia datos de iluminación horneada
(`m_LightingDataAsset` vacío); la luz direccional es Mixed y alumbra en tiempo real.

Se eliminaron los objetos del blockout anterior: las dos casas `House_01_cyber`, el muro de
ProBuilder `wall` y el `Enemy`, que flotaban a 35–45 m (T21), y el `Ground` hecho con una malla
Capsule (cúpula de 0.5 m, T19). La validación comprueba que no queda ningún collider fuera del área.

| Objeto | Qué es |
|---|---|
| `Suelo` | Caja de 62 × 80 m con la cara superior en y = 0 (layer Ground), x −34…28, z 15…−65. |
| `Perimetro` | Muros de 1.5 m alrededor del suelo, en layer **Ground**: demasiado altos para el vault, demasiado bajos para agarrarse, y nunca candidatos a cornisa (las cornisas solo se buscan en Obstacle). |
| `S01_Locomocion` … `S07_Combinado` | Carriles paralelos que empiezan en z = 0 y avanzan hacia −Z. Contenido en `features.md` F32. |
| `Spawner` | (0, 1.2, 8), mirando a −Z, en la entrada del área. |

Layers del área: el suelo, las escaleras, los bordillos y las plataformas de aterrizaje son
**Ground** (el auto step puede subirlos, y no son vault ni cornisa); los obstáculos de vault, los
muros de cornisa y escalada, las barras del slide y los pilares son **Obstacle**.

### Prefabs y sus componentes

| Prefab | Componentes de scripts propios | Notas |
|---|---|---|
| `Player` | `Player`, `PlayerMovement`, `PlayerInputHandler`, `PlayerAnimator`, `PlayerContactIK`, `GroundChecker`, `EnvironmentChecker`, `HealthSystem` (i-frames 0.5 s), `Hitbox` (radio 0.6, `localOffset` z = 0.6, layer Enemy), `WeaponHolder` (sin arma inicial); en `Model`: `PlayerAnimatorIK` | Tag `Player`, layer 8. Rigidbody 70 kg, **Interpolate**, rotaciones congeladas. Hijos `CenterPoint`, `HeadPoint`, `Model` (character.fbx, en y = −0.974: las suelas de la pose idle tocan la base del collider). El `Animator` de `Model` usa `PlayerAnimator.controller`, culling **Always Animate** y Apply Root Motion desactivado por defecto (`PlayerAnimator` lo activa solo en parkour, P22). `stepLayer`, `ceilingLayer`, `landingLayer` y las capas de `PlayerContactIK` = Ground + Obstacle (los muros del IK, solo Obstacle). |
| `Enemy` | `HealthSystem` (100 HP, i-frames 0.2 s), `Hitbox` (daño 5) | Tag `Untagged`, layer 9. ⚠️ **No tiene `Enemy.cs`**, así que la IA no corre, y `Hitbox.targetLayers = 0`. La vida y el daño no son valores del GDD: el prefab se rehace con P4. Desde P24 no está colocado en ninguna escena. |
| `GameManager` | `ObjectPoolManager` | Pools: `player` → Player.prefab (1), `spawnVFX` → Particle System (1). |
| `Spawner` | `Spawner` | `entityId: player`, `triggerType: OnStart`. |
| `Main Camera` | `CameraFollow` | `autoDetectTarget` activo. |
| `Particle System` | `ReturnToPoolDelay` | VFX de spawn pooleado. |
| `Directional Light` | — | Luz Mixed, intensidad 1 (antes 0.3). |

## 4. Mapa de dependencias

```mermaid
flowchart LR
    subgraph Player.prefab
      PIH[PlayerInputHandler<br/>IInputProvider] --> P[Player<br/>Facade]
      P --> PM[PlayerMovement<br/>contexto de la FSM]
      PM --> FSM[PlayerStateMachine<br/>+ 12 estados]
      PM -- evento StateChanged --> PA[PlayerAnimator]
      PA --> AN[Animator de Model<br/>PlayerAnimator.controller]
      PA -- root motion en parkour --> PM
      PA --> CIK[PlayerContactIK<br/>IK de manos y pies]
      PA -. IParkourAnimationProgress .-> PM
      HS -. IDamageModifier .-> PM
      PM --> GC[GroundChecker<br/>IGroundChecker]
      PM --> EC[EnvironmentChecker]
      FSM -. GetComponent en constructor .-> HB[Hitbox]
      FSM -. GetComponent .-> HS[HealthSystem<br/>IDamageable]
      FSM -. GetComponent .-> WH[WeaponHolder]
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
para no fallar. Los tres (`Hitbox`, `HealthSystem`, `WeaponHolder`) están en el Player.prefab.

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
  `EnvironmentChecker`, `HealthSystem` y `Camera.main.transform` en `Awake`, y construye las 12
  instancias de estado. Expone el estado de input (`InputX`, `InputZ`, `HasMoveInput`,
  `IsSprintHeld`, `JumpTriggered`, …), `AirTime` (segundos sin suelo), `AirPeakFeetY` (punto más
  alto desde que dejó el suelo), `IsBackpedaling`, `FeetY`, `HorizontalSpeed`,
  `StandingHalfHeight`, `PendingVault`, `CurrentLedge`, `ParkourProgress`, el evento
  `StateChanged`, el evento `Stepped` y helpers de física (`SetVelocity(Vector3, float)`,
  `StopHorizontal(float)`, `AccelerateHorizontal(Vector3)`, `AccelerateAir(Vector3)`,
  `FaceDirection`, `TryAutoStep`, `TryStepDown`, `SetKinematic`, `ShrinkCollider`,
  `ResetCollider`, `HasCeilingOverhead`, `BeginRootMotion`, `ApplyRootMotion`, `EndRootMotion`,
  `RegisterLanding`). Velocidades: `BaseSpeed` 5 (correr), `SprintSpeed = BaseSpeed ×
  SprintMultiplier (1.4)`, `BackpedalSpeed` 1.5, `JumpSpeed` 4.5 (~1 m de salto); `Acceleration`
  10 m/s² y `Deceleration` 13 m/s² en el suelo; `AirAcceleration` 4 m/s² y `AirDrag` 0.5 m/s² en
  el aire; slide: entra con la velocidad que lleva (máximo `SlideSpeed` 7.5) y pierde
  `SlideFriction` 4 m/s² hasta `SlideMinSpeed` 2.5 (P23). Implementa `IDamageModifier` y lo delega
  en `BlockState` (ver §5.4). Escucha `HealthSystem.OnDamageReceived` para cancelar el sprint.
  **Regla:** los estados mueven el cuerpo con estos helpers. Mientras `IsRootMotionDriven` (vault,
  cornisa, subida), el cuerpo es kinemático, sin interpolación, y lo mueve la animación a través de
  `ApplyRootMotion` (ver §5.10).
- **Flujo de un tick:** `LogicUpdate` y `PhysicsUpdate` corren los dos dentro del paso de física
  (el input se enruta desde `Player.FixedUpdate`), así que la lógica de estados corre a 50 Hz y no
  por frame. El input se captura en `Update` y se **bufferiza** con métodos `Consume*()`, para que un
  tap no se pierda entre frames ni se procese dos veces.
- **Movimiento relativo a cámara:** `MoveDirection` es el input proyectado sobre el
  forward/right aplanado de la cámara. La rotación usa `Mathf.SmoothDampAngle` aplicado con
  `Rigidbody.MoveRotation` (interpolado como el movimiento); el tiempo de giro pasa de
  `turnSmoothTime` (0.12 s) corriendo a `sprintTurnSmoothTime` (0.2 s) esprintando, porque un cuerpo
  rápido gira más abierto. Se bloquea en Vault, LedgeGrab, LedgeClimb, Slide, Block, Dodge y los
  ataques. **Caminar hacia atrás** (`IsBackpedaling`: input con componente atrás y sin sprint, en
  `Run`): el cuerpo mira al forward aplanado de la cámara en lugar de girar hacia `MoveDirection`, y
  avanza a `BackpedalSpeed`.
- **Aceleración:** `Run` y `Idle` no escriben la velocidad de golpe: `AccelerateHorizontal` la lleva
  hacia la velocidad objetivo con `Acceleration`/`Deceleration`, de modo que el blend de locomoción
  pasa por Walk y Jog al arrancar y al frenar. En el aire, `AccelerateAir` solo corrige el impulso
  con el input (`AirAcceleration`); sin input lo conserva casi intacto.
- **Aterrizaje (P23):** al tocar el suelo, `Fall` llama `RegisterLanding`: la caída desde
  `AirPeakFeetY` define una severidad (0 bajo 0.6 m, 1 hacia 3.2 m). En ese instante se absorbe
  parte de la velocidad horizontal y el resto vuelve durante la recuperación
  (`RecoverySpeedScale`, hasta 0.7 s). El control nunca se bloquea.
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
| `PlayerGroundedStates.cs` | `Idle`, `Run` (sprint con Shift), `Slide` |
| `PlayerAirStates.cs` | `Jump` (subida), `Fall` (caída) |
| `PlayerParkourStates.cs` | `Vault`, `LedgeGrab`, `LedgeClimb` |
| `PlayerCombatStates.cs` | `LightAttack`, `HeavyAttack`, `Block`, `Dodge` |

```mermaid
stateDiagram-v2
    Idle --> Run: input
    Idle --> Vault: Espacio + obstáculo bajo delante
    Idle --> LedgeGrab: Espacio + cornisa a 1.9–2.7 m
    Idle --> Jump: Espacio + suelo
    Idle --> Fall: sin suelo > 0.15 s
    Idle --> Block: L
    Idle --> Dodge: Q + suelo + cooldown 1 s
    Idle --> LightAttack: J
    Idle --> HeavyAttack: K
    Run --> Vault: Espacio + obstáculo bajo delante
    Run --> LedgeGrab: Espacio + cornisa a 1.9–2.7 m
    Run --> Slide: C + sprint
    Run --> Dodge: Q + cooldown 1 s
    Run --> Jump: conserva el sprint
    Run --> Fall: sin suelo > 0.15 s, conserva el sprint
    Run --> Idle: sin input
    Jump --> LedgeGrab: cornisa al alcance de las manos
    Jump --> Fall: apex en el aire
    Jump --> Idle: apex ya en el suelo
    Fall --> LedgeGrab: cornisa al alcance de las manos
    Fall --> Run: aterriza con input (RegisterLanding)
    Fall --> Idle: aterriza sin input (RegisterLanding)
    LedgeGrab --> LedgeClimb: Espacio (también pulsado durante el agarre)
    LedgeGrab --> Fall: dirección contraria al muro
    LedgeClimb --> Run: clip al 97 %, con input
    LedgeClimb --> Idle: clip al 97 %, sin input
    Vault --> Run: clip al 82 %, con input
    Vault --> Idle: clip al 82 %, sin input
    Slide --> Run: 0.8 s sin techo, con input
    Slide --> Idle: 0.8 s sin techo, sin input
    LightAttack --> LightAttack: J en ventana (máx 3)
    LightAttack --> HeavyAttack: K en ventana
    LightAttack --> Idle
    HeavyAttack --> Run: 0.8 s con input
    HeavyAttack --> Idle: 0.8 s sin input
    Block --> Idle: suelta L
    Block --> LightAttack: contraataque
    Dodge --> Run: 0.5 s
    Dodge --> Idle: 0.5 s
```

> **Sprint (GDD §5.2):** en `Run`, `IsSprint = CanSprint` en cada tick (Shift mantenido y sprint no
> cancelado). `CancelSprint()` lo apaga al atacar, bloquear o recibir daño y no se reactiva hasta
> soltar Shift. `IsSprint` no se limpia al pasar a `Jump`, `Fall` o `Vault`, así que conservan el
> impulso (GDD §5.2, §5.4); `Idle.Enter` lo reinicia.
>
> **Caída:** `PlayerFallState` cubre la caída después del apex del salto y la caída al salir de una
> orilla (`AirTime > FallGraceTime`, 0.15 s, para no reaccionar a escalones). Ahí irá la caída
> mortal (F15).

### 5.2 Input — ✅ Implementado (modo prototipo)

`PlayerInputHandler : IInputProvider`. Cada acción tiene dos caminos: un `InputActionReference`
opcional (todos vacíos en el prefab) o la lectura directa de `Keyboard.current`.
**Hoy se usa la lectura directa**, con los bindings del GDD §14 (P1): WASD/flechas, Shift (sprint
mantenido, `IsSprintHeld`), Espacio, J, K, L (mantener), Q y C (slide). El ratón ya no se usa. La documentación oficial del Input System la describe como
adecuada solo para prototipos, porque se salta el rebinding, los esquemas de control y el gamepad.
No hay ningún asset `.inputactions` en el proyecto (la plantilla por defecto se eliminó; P6).

### 5.3 Detección de entorno — ✅ Implementado

- **`GroundChecker : IGroundChecker`**: un `Physics.SphereCast` corto hacia abajo con radio del 90 %
  del collider, desde justo encima de los pies, contra `groundLayer` (Ground + Obstacle). Cubre la
  huella de los pies, no solo un rayo bajo el centro, así que el cuerpo sigue "en el suelo" sobre el
  borde de un escalón en lugar de parpadear al aire.
- **`EnvironmentChecker`**: pre-filtro `Physics.OverlapSphereNonAlloc` (buffer de 4, radio 1.4)
  antes de los raycasts, y una comprobación de espacio libre (`CheckCapsule` de un cuerpo de pie).
  Todo lo que devuelve está medido sobre la geometría real.
  - **Vault (`TryFindVault`, adaptado del Dynamic Parkour System, ver §5.12):** rayos sin
    allocations en layer Obstacle: (1) cara frontal a la altura de la rodilla dentro de `vaultReach`
    (1.1 m) y de frente (`Dot ≥ 0.6`); **la dirección del vault es perpendicular a la cara**, no la
    del input; (2) cima entre `minVaultHeight` (0.45 m) y `vaultHeightCheck` (1.2 m) sobre los pies;
    (3) profundidad ≤ `maxVaultDepth` (1.5 m), con un rayo de regreso desde detrás; (4) aterrizaje:
    prueba a 1.6 m de la cara trasera (donde aterriza el clip corriendo), luego 1.1 y 0.6 m, y acepta
    el primero con suelo y espacio para estar de pie. Devuelve un `VaultInfo`: dirección, punto de
    aterrizaje, punto de la mano izquierda (sobre la cima, 0.12 m tras el borde y 0.3 m a la
    izquierda, como en el clip), altura de la cima, borde frontal, profundidad y distancia a la mano.
  - **Cornisa (`TryFindLedge`):** cara del muro con rayos a 1.2 m y 1.75 m sobre los pies (alcance
    1.0 m desde el suelo, 0.75 m en el aire) que mire al jugador (`Dot ≥ 0.5`); cima plana entre una
    altura mínima y máxima sobre los pies (1.9–2.7 m desde el suelo, 1.5–2.6 m en el aire); el
    **borde exacto** con un rayo horizontal justo bajo la cima; y espacio para estar de pie en el
    punto donde termina la subida (0.45 m tras el borde). Devuelve un `LedgeInfo`: borde, normal,
    cima, punto de pie, rotación de frente al muro y `EdgeAt(punto)` (el borde a la altura lateral
    de cualquier mano).
- **Auto step (`PlayerMovement.TryAutoStep`, adaptado del DPS):** en `Run`, si un rayo a 5 cm del
  suelo choca en la dirección de movimiento y otro a `StepHeight` (0.4 m) no, busca la cima con un
  rayo hacia abajo y sube el cuerpo hasta ella (`stepLayer` = Ground + Obstacle). Los obstáculos de
  vault (≥ 0.45 m) no se suben así. **Step down (`TryStepDown`):** al bajar un escalón de hasta
  0.4 m, el cuerpo baja con él en lugar de pasar a `Fall` en cada peldaño (no actúa durante 0.3 s
  después de subir un escalón). Los dos disparan `Stepped`, y `PlayerAnimator` suaviza el modelo en
  0.1 s para que el salto de posición no se vea.

### 5.4 Combate — 🟡 Parcial

| Script | Responsabilidad |
|---|---|
| `HealthSystem : IDamageable` | Vida con clamp, i-frames por tiempo (`iFramesDuration`: 0.5 s el jugador, 0.2 s el enemigo para que entren los golpes del combo, que van cada ≥ 0.25 s), `ActivateIFrames(d)` (nunca acorta unos i-frames ya activos), `Heal` (no revive), `InstantKill`, `InitializeHealth(max)` (reinicia y revive). Eventos `OnHealthChanged`, `OnDeath`, `OnDamageReceived`. Sin `Update`. |
| `Hitbox` | `Activate()` hace un `OverlapSphereNonAlloc` (buffer de 10) centrado en `Center` (`transform` + `localOffset`) contra `targetLayers` y llama `TakeDamage` en cada `IDamageable`. `SetDamage`, `SetRadius`, evento `OnHit`. Es un pulso instantáneo, no un trigger persistente. En el jugador la esfera está 0.6 m al frente del torso. |
| `WeaponData` (SO) | Nombre, icono, daño ligero/pesado, radio de hitbox, knockback, `weaponId`. Menú `Create > WarriorWoke > Weapon Data`. **No hay assets creados.** |
| `WeaponHolder` | Arma equipada, `Equip(data)`, `GetLightDamage()` (10 desarmado), `GetHeavyDamage()` (20 desarmado), `GetKnockback()`, evento `OnWeaponChanged`. Está en el Player.prefab sin arma inicial. |

**Bloqueo (GDD §5.8):** `HealthSystem.Awake` busca un `IDamageModifier` en su GameObject y, si
existe, lo llama en `TakeDamage` (después del chequeo de i-frames). En el Player lo implementa
`PlayerMovement`, que delega en `PlayerBlockState.ModifyIncomingDamage` solo mientras el estado
actual es `Block`: si la fuente está dentro de ±60° del frente (`Dot ≥ 0.5`, en XZ), el daño se
multiplica por 0.3 (−70 %, redondeado); si no, pasa completo. Los enemigos no tienen modificador.

Limitaciones reales: `Hitbox` no verifica ángulo ni evita pegarle dos veces al mismo objetivo si
tiene varios colliders y el knockback no se aplica.

### 5.5 Enemigos — 🟡 Parcial / ⚠️ desalineado con el GDD

- **`Enemy : MonoBehaviour, IPoolable`**: el mismo patrón de contexto + FSM que el jugador, con una
  diferencia: `LogicUpdate` corre en `Update` y `PhysicsUpdate` en `FixedUpdate`. Lee sus valores de
  un `EnemyData` (SO). Estados en `EnemyGroundedStates.cs`: `Patrol`, `Chase`, `Attack`, `Dead`.
  La detección es un `OverlapSphereNonAlloc` filtrado por `playerLayer` (por defecto la layer
  `Player`) + `CompareTag("Player")` + raycast de línea de visión. Al morir espera 1.5 s y vuelve
  al pool; `OnSpawn` lo revive con `InitializeHealth`.
- ⚠️ **Es 2.5D:** congela Z, se mueve solo en X (`Move(float dir)`) y rota a ±90°. El jugador ya es
  3D libre, así que los enemigos **no pueden perseguirlo en profundidad**. Se reescribe con P4.
- ⚠️ No hay subclases por tipo: `Looter`/`Brute` (GDD anterior) se eliminaron; los tipos del GDD
  (arquero, guerrero ligero, guerrero pesado) llegan con P4.
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
- `Spawn` coloca el objeto (posición, rotación, sin padre) **antes** de activarlo, para que los
  `OnEnable` (p. ej. `OnPlayerSpawned` → snap de la cámara) vean la posición final y el Rigidbody
  no se teletransporte por su transform.
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

### 5.8 Sistemas eliminados — no reintroducir

| Sistema | Motivo | Fecha | Decisión |
|---|---|---|---|
| XP (`PlayerXpSystem`, `IXpReceiver`, `EnemyData.xpReward`) | GDD §7, §19: progresión solo por historia | 2026-09-29 | P3 |
| Wall jump (`PlayerWallJumpState`, `ConsecutiveWallJumps`, `EnvironmentChecker.IsTouchingWall`) | Fuera del GDD §28 y sin uso | 2026-09-30 | P2, P8 |
| Estamina y HUD (`PlayerStamina`, `PlayerHUD`) | GDD §5.2 (sprint sin recurso) y §16 (sin barras de vida) | 2026-09-30 | P7 |
| Enemigos `Looter`/`Brute` | GDD anterior; los tipos del GDD final llegan con P4 | 2026-09-30 | P8 |
| `initial_floor.prefab`, `InputSystem_Actions.inputactions` | Sin uso | 2026-09-30 | P8 |
| Blockout anterior de `Level-1` (casas `House_01_cyber`, `wall`, instancia de `Enemy`, `Ground` cápsula) y carteles del circuito | `Level-1` es el Parkour Test Area | 2026-10-01 | P24 |
| `LowPoly/HumanPlayer.prefab`, `LowPolyHumanAnimator.controller`, `ball.mat`, `metal.mat`, `Sign.mat`, 10 FBX Ch45 de la raíz | Sin uso ni referencias | 2026-10-01 | P8, P24 |

### 5.9 Herramientas de Editor — ✅ Implementado

- `SceneAutoLoader` (`[InitializeOnLoad]` + `SessionState`): abre `Level-1.unity` una vez por sesión.
- `PlayerCharacterSetup` (menú **Tools → Warrior Woke → Configurar Modelo del Jugador**): configura
  `character.fbx` como Humanoid (o Generic), lo pone como hijo `Model` del Player.prefab, lo
  reescala si hace falta y ajusta `CapsuleCollider` y `HeadPoint`. Es idempotente.
- `PlayerAnimationSetup` (menús **Tools → Warrior Woke → Configurar Animaciones del Jugador** y
  **Validar Personaje**; también en batch con `-executeMethod
  WarriorWoke.EditorTools.PlayerAnimationSetup.SetupAndValidateBatch` o `ValidateBatch`): extrae
  las texturas embebidas de `character.fbx`, importa las transiciones de guardia Ch45, configura el
  root motion de cada clip que usa el jugador (§5.10) y restaura la curva `LHandCurve`, regenera
  `PlayerAnimator.controller` (conserva su GUID) y lo asigna al prefab: coloca el `Model` con las
  suelas en la base del collider (medidas con `SkinnedMeshRenderer.BakeMesh` sobre la pose idle),
  activa la interpolación del Rigidbody, el IK Pass y agrega `PlayerAnimatorIK` y
  `PlayerContactIK`. La validación revisa Avatars, material, clips de cada estado, la curva del
  vault, el contacto de las suelas, Missing Scripts (prefab y `Level-1`), el Parkour Test Area y
  muestrea cada clip sobre Ch45 con `AnimationMode` en 5 momentos para detectar poses rotas (73
  comprobaciones). Es idempotente. El batch construye además el área.
- `ParkourTestCircuitBuilder` (menús **Construir Parkour Test Area** y **Listar límites de la
  escena**): reconstruye `ParkourTestArea` en `Level-1`, elimina los objetos del blockout anterior
  si siguen ahí, coloca el `Spawner` en la entrada y valida que no quede ningún collider fuera del
  área. Ver `features.md` F32.
- `ParkourPlayModeTest` (menú **Probar Personaje en Play Mode**; batch sin `-quit`:
  `-executeMethod WarriorWoke.EditorTools.ParkourPlayModeTest.RunBatch`): entra a Play Mode, agrega
  un teclado virtual del Input System y recorre las siete secciones. Además del flujo de estados,
  mide el contacto sobre el esqueleto animado (140 comprobaciones; detalle en `features.md` F32).
  Sale con código 0 si todo pasa.

### 5.10 Animación y contacto físico — 🟡 Parcial (funciona, con clips provisionales)

**Modelo y materiales.** `character.fbx` es el personaje Ch45 de Mixamo (rig `mixamorig1:`, 1
material `Ch45_Body`), importado como **Humanoid** con su propio Avatar. Sus 5 texturas (Diffuse,
Normal, Specular, Glossiness, Emissive, 2048²) venían **embebidas** en el FBX y Unity no las usa
hasta extraerlas: por eso el personaje se veía sin textura. Se extrajeron a
`Characters/Player/Textures/` (Normal → tipo *Normal Map*, Glossiness → lineal) y el importador de
materiales de URP las asigna al material del FBX (`_BaseMap` = Diffuse, `_BumpMap` = Normal,
emisión activa). El material del FBX se conserva; no hay materiales propios.

**Colocación del modelo.** El `Model` se había centrado con los bounds del `SkinnedMeshRenderer`,
que traen margen, y de pie las suelas quedaban **11 cm sobre el suelo**. Ahora `PlayerAnimationSetup`
mide el vértice más bajo de la pose idle y coloca el modelo en y = −0.974, con las suelas en la base
del collider.

**Arquitectura.** La FSM es la única fuente de verdad (D10); el parkour usa root motion (P22):

```
PlayerStateMachine.OnStateChanged → PlayerMovement.StateChanged → PlayerAnimator
PlayerAnimator: CrossFadeInFixedTime(estado del Animator, 0.2 s por defecto; 0.05–0.1 s en ataques,
                aterrizajes y parkour; 0.3 s hacia la caída) + Speed cada frame + MatchTarget
Model/PlayerAnimatorIK.OnAnimatorMove → PlayerAnimator.ApplyRootMotion → PlayerMovement.ApplyRootMotion
Model/PlayerAnimatorIK.OnAnimatorIK   → PlayerAnimator.ApplyIK → PlayerContactIK.Solve
PlayerAnimator implementa IParkourAnimationProgress → PlayerMovement.ParkourProgress (lo leen los estados)
```

- `PlayerAnimator` (en la raíz del Player) busca el `Animator` de `Model` y traduce cada estado de
  la FSM a un estado del Animator. Los estados de la FSM no llaman al Animator: leen el progreso del
  clip de su acción por la interfaz `IParkourAnimationProgress` y terminan o encadenan en momentos
  medidos (`ParkourTimings`), no tras un tiempo fijo.
- `PlayerAnimatorIds` guarda nombres y hashes (`Animator.StringToHash`, una sola vez); los comparten
  `PlayerAnimator` y la herramienta de Editor.
- `Speed` es positiva salvo al caminar hacia atrás (velocidad a lo largo del frente / `BaseSpeed`),
  así que un giro brusco a velocidad nunca muestra la caminata hacia atrás.

**Root motion por clip (P22).** Como `PlayerAnimatorIK` implementa `OnAnimatorMove`, Unity nunca
aplica el root motion por su cuenta: lo decide `PlayerAnimator`.

| Modo | Clips | Qué pasa |
|---|---|---|
| En el sitio (el avance horizontal no queda en la pose; la altura sí) | Walk, Jog Forward, Run, Fall A Loop, Falling To Landing, Fall A Land To Run Forward, Slide Down, Slide, Slide Up | El Rigidbody mueve el cuerpo. Antes Walk y Jog tenían horneado en la pose un avance de 1.6–2.2 m por ciclo: la pose patinaba y regresaba en cada ciclo. |
| Root motion (horizontal según el centro de masa) | Vault1, Idle To Braced Hang, Braced Hang To Crouch, Jump_Up | Vault, agarre y subida: `PlayerAnimator` aplica el movimiento del clip al cuerpo (kinemático) y lo warpea con `MatchTarget`. Jump_Up: su subida no se aplica (la hace la física), así la pose ya no flota 0.58 m sobre el collider. "Centro de masa": Vault1 empieza a mitad de una carrera y, con la base "Original", su pose arrancaba 0.9 m por delante del cuerpo. |
| Todo en la pose | Hanging Idle, transiciones de guardia Ch45, clips LowPoly en el sitio | Bucles y clips sin desplazamiento. |

Los ajustes de importación se escriben con `SerializedObject` sobre `m_ClipAnimations`: en esta
versión de Unity el getter `ModelImporter.clipAnimations` falla (`Cannot unmarshal intptr objects in
structs`) y devuelve los clips sin sus curvas. Al reescribirlos se perdió `LHandCurve` (el peso de la
mano del vault), así que el IK de la mano del vault nunca había funcionado. Ahora se restaura desde
el `.meta` original del DPS (T22).

**MatchTarget (warp del root motion hacia el contacto medido).** Unity acepta un match a la vez;
`PlayerAnimator` los encola por fases. Las rotaciones no se warpean (peso 0, como en el DPS):
`ApplyRootMotion` gira el cuerpo a 540°/s hasta quedar de frente a la cara del obstáculo o del muro.

| Acción | Fase | Parte | Destino | Ventana (tiempo normalizado del clip) |
|---|---|---|---|---|
| Vault | Despegue (solo si la cima supera 0.8 m) | Raíz, solo Y | Sube lo que el obstáculo excede, antes de que llegue la pierna delantera | inicio → 0.16 |
| Vault | Apoyo | Mano izquierda | Punto de la mano sobre la cima (+ altura de la muñeca) | inicio → 0.30 |
| Vault | Aterrizaje | Raíz | Punto de aterrizaje medido | 0.53 → 0.78 |
| LedgeGrab | Agarre | Mano izquierda | Borde medido (+ muñeca, a la izquierda del cuerpo) | 0.30 desde el suelo, o desde la entrada en el aire → 0.56 |
| LedgeClimb | Cima | Raíz | Punto de pie sobre la cima | 0.40 → 0.95 |

El vault se reproduce a la velocidad de la aproximación (`ParkourSpeed` = velocidad / 4.35 m/s del
clip, entre 0.8 y 1.5) y empieza más tarde si el obstáculo está más cerca de lo que espera el clip.
El agarre desde el suelo reproduce el clip desde 0.12 (incluye el salto); en el aire entra en 0.40,
con los brazos ya arriba.

**`PlayerContactIK` (IK Pass Humanoid).** Mantiene manos y pies sobre las superficies reales:

| Situación | Manos | Pies |
|---|---|---|
| Vault | Izquierda sobre la cima, con el peso de `LHandCurve` | Un pie que pasa sobre el obstáculo (o está por entrar) nunca baja de la cima |
| Colgado | Ambas en el borde, a su altura lateral; el cuerpo se desplaza para que la mano animada llegue sola y el IK solo corrija los últimos centímetros | Apoyados en el muro (tobillo a 0.13 m); un pie dentro del muro siempre se saca |
| Subida | En el borde mientras tira del cuerpo (el cuerpo sigue a las manos), luego apoyadas sobre la cima sin atravesarla | En el muro al inicio, luego libres |
| En el suelo (Idle, Run, Slide, Block, ataques) | — | Siguen el terreno (escalones, bordillos) y nunca se hunden en él; la pelvis baja hasta 0.35 m si un pie pisa más abajo (enfoque del IK de pies del DPS). Se aplica de inmediato al aterrizar y se apaga al entrar en una acción de parkour. |

**`PlayerAnimator.controller`** (generado por `PlayerAnimationSetup`, IK Pass activo). Parámetros:
`Speed`, `DodgeX`, `DodgeY`, `LHandCurve` (lo escribe la curva del clip de vault) y `ParkourSpeed`
(velocidad del estado Vault, 1 por defecto). Transiciones propias, todas por exit time hacia un
bucle o hacia la locomoción: `BlockEnter → BlockLoop`, `BlockExit → Locomotion`,
`Land / LandRun / LandHard → Locomotion`, `Slide → SlideLoop`, `SlideExit → Locomotion`,
`LedgeGrab → LedgeHang`. El resto los decide la FSM. 20 estados.

| Estado del Animator | Estado FSM | Clip (origen) | Velocidad |
|---|---|---|---|
| `Locomotion` (Blend 1D por `Speed`) | Idle, Run | RunBackward −0.3 (LowPoly, ×0.6) · Idle 0 (LowPoly) · Walk 0.34 · Jog Forward 0.52 · Run 1 (DPS) · Sprint 1.4 (LowPoly) | 1 |
| `Jump` | Jump | Jump_Up — LowPoly | 1 |
| `Fall` | Fall | Fall A Loop — DPS | 1 |
| `Land` / `LandRun` / `LandHard` | (al aterrizar, según la severidad) | Falling To Landing / Fall A Land To Run Forward / Falling To Landing — DPS | 0.45 s / 0.4 s / 0.9 s |
| `Vault` | Vault | Vault1 (VaultFence) — DPS, root motion, curva `LHandCurve` | `ParkourSpeed` |
| `Slide` → `SlideLoop`; `SlideExit` | Slide; al salir del slide | Slide Down → Slide (bucle); Slide Up — DPS | 0.3 s → 1; 0.5 s |
| `LedgeGrab` → `LedgeHang` | LedgeGrab | Idle To Braced Hang (root motion) → Hanging Idle — DPS | 1.1 / 1 |
| `LedgeClimb` | LedgeClimb | Braced Hang To Crouch (root motion) — DPS | 1.1 |
| `LightAttackRight` / `LightAttackLeft` | LightAttack (golpes 1-3 / 2) | PunchRight / PunchLeft — LowPoly | ajustada a 0.5 s |
| `HeavyAttack` | HeavyAttack | MeleeAttack_OneHanded — LowPoly (**placeholder: no hay patada**) | ajustada a 0.8 s |
| `BlockEnter` → `BlockLoop` → `BlockExit` | Block | Standing Idle To Fight Idle (Ch45) → BlockingLoop (LowPoly) → Fight Idle To Standing Idle (Ch45) | transiciones a 0.3 s |
| `Dodge` (Blend 2D `DodgeX`/`DodgeY`) | Dodge | RollForward/Backward/Left/Right — LowPoly | ajustada a 0.5 s |

Aterrizaje: severidad < 0.35 → `LandRun` con input o `Land` sin input; 0.35–0.75 → `Land`;
≥ 0.75 → `LandHard` (absorción completa, y la transición a la locomoción dura 0.3 s).

Los umbrales del blend son la velocidad del clip / `BaseSpeed`: Walk (1.7 m/s) y Jog (2.6 m/s) se
midieron con el desplazamiento de la cadera de los clips del DPS; caminar hacia atrás es
`BackpedalSpeed` (1.5 m/s).

**Clips que existen pero no se usan:** de LowPoly, `GetHit`, `Death`, `IdleCombat`, `StunnedLoop`,
`BowShot`, `Buff`, `CastingLoop`, `SpellCast`, `Gathering`, `MiningLoop`, `MeleeAttack_TwoHanded`,
`RunForward`, `FallingLoop`, `JumpWhileRunning`, `Jump_Down`, `Run*`/`Strafe*` direccionales.
`GetHit`/`Death` tendrán uso con F19/F29. De Ch45, solo están en el proyecto las 2 transiciones de
guardia (los demás FBX descargados se borraron el 2026-10-01). Dentro de `Slide.fbx`, *Running
Slide* (el slide completo) se dejó de usar al pasar a las tres fases.

### 5.11 Sistemas que no existen todavía — ⬜ Pendiente

Checkpoints, muerte/reaparición, caída mortal, regeneración de vida, recoger armas, IA del arquero
(ataque a distancia), jefes, zonas de enemigo, guardado, menú principal, pausa, flujo de escenas y
niveles, audio, feedback de daño (VFX, camera shake, estado visual de salud) y build de Windows.

### 5.12 Parkour: integración del Dynamic Parkour System — ✅ Implementado (parcial respecto al recurso)

Recurso: **Dynamic Parkour System** de Èric Canela, licencia MIT, animaciones de Mixamo
(`Recursos/Dynamic-Parkour-System-main`, Unity 2019.4, depende de Cinemachine 2.6 e Input System).
Es un controlador completo (`ThirdPersonController` exige `InputCharacterController`,
`MovementCharacterController`, `AnimationCharacterController`, `DetectionCharacterController`,
`CameraController` con Cinemachine y `VaultingController`) y todas sus acciones dependen de él.

**Decisión P15: adaptar, no reemplazar.** No se importó ningún script del recurso. Se portó su
lógica a nuestra FSM y se usan 11 de sus FBX (13 clips):

| Del DPS | En el proyecto |
|---|---|
| `VaultObstacle`: rayo de rodilla → aterrizaje detrás → cuerpo movido durante el clip, IK de mano izquierda con la curva `LHandCurve` | `EnvironmentChecker.TryFindVault` + `PlayerVaultState` + `PlayerContactIK`. Mide la cara, la altura y la profundidad con rayos (el original usa `localScale` y tags). El cuerpo lo mueve el root motion del clip warpeado con `MatchTarget` (el original interpola la posición), con la fase extra de despegue. |
| `AnimationCharacterController`: root motion activo en estados con tag "Root" y `MatchTarget` | `PlayerAnimator`: root motion solo en Vault, LedgeGrab y LedgeClimb, aplicado por `OnAnimatorMove` al cuerpo kinemático, y `MatchTarget` por fases (§5.10). |
| `ClimbController` (braced hang): `MatchTarget` de la mano al agarrar y del pie al subir; IK de manos en el borde y de pies en el muro | `PlayerLedgeGrabState` / `PlayerLedgeClimbState` + `TryFindLedge` + `PlayerContactIK`. Agarre desde el suelo (como el original) o en el aire; la subida warpea la raíz hacia el punto de pie medido. Animaciones *Idle To Braced Hang*, *Hanging Idle* y *Braced Hang To Crouch*. |
| `MovementCharacterController`: `AutoStep`, IK de pies con ajuste de pelvis | `PlayerMovement.TryAutoStep` (sube hasta la cima medida, no 0.2 m por tick) y `TryStepDown`; IK de pies en el suelo en `PlayerContactIK`, con la regla extra de no penetración. |
| `VaultSlide` (slide bajo obstáculos) | Se conservó nuestro `PlayerSlideState` (collider al 50 %, no se levanta bajo techo); se usan los sub-clips *Slide Down*, *Slide* y *Slide Up*. |
| Animaciones de locomoción y aire | *Walk*, *Jog Forward*, *Run*, *Fall A Loop*, *Falling To Landing*, *Fall A Land To Run Forward* |

**No se usó (fuera del GDD §28, que limita el parkour a salto, sprint y vault):** escalar paredes
y obstáculos (`HandlePoints`), saltar a postes y la predicción de saltos
(`JumpPredictionController`, `VaultJumpPrediction`), salto de cornisa a cornisa, *drop* a cornisa
(`VaultDown`), free hang, desplazamiento lateral colgado, *Vault Over* (caja) y *Reach*.
**Tampoco:** su cámara Cinemachine (usamos `CameraFollow`), su input (usamos `PlayerInputHandler`)
ni su prevención de caídas en bordes (`CheckBoundaries`), que cambiaría el control. Las curvas de
pies de Walk/Jog/Run del original no se restauraron: nuestro IK de pies no las necesita.

## 6. Decisiones técnicas vigentes

| # | Decisión | Motivo |
|---|---|---|
| D1 | Jugador con **Rigidbody dinámico** + velocidad escrita por los estados (no `CharacterController`). En parkour el cuerpo pasa a kinemático y lo mueve el root motion (P22) | Ya implementado y funcionando; el modo kinemático permite que la animación lleve el cuerpo sin pelear con la física. |
| D2 | **FSM por clases** (una clase por estado, transiciones dentro del estado) para jugador y enemigos | Coincide con lo que pide el GDD para la IA (§21) y mantiene los estados testeables y aislados. |
| D3 | **Facade** (`Player`) + **contexto** (`PlayerMovement`) | Separa el enrutado de input de la física y la lógica. |
| D4 | **Interfaces** para desacoplar (`IDamageable`, `IInputProvider`, `IGroundChecker`, `IPoolable`) | `Hitbox` no conoce al jugador ni al enemigo; el input se puede cambiar sin tocar el movimiento. |
| D5 | **ScriptableObjects** para datos de diseño (`WeaponData`, `EnemyData`) | Los datos se comparten entre instancias y se ajustan sin tocar código. Son de **solo lectura en runtime** en builds. |
| D6 | **Object pooling** para todo lo que nace y muere seguido | Evita GC e `Instantiate` en el game loop. |
| D7 | **Eventos C# (`System.Action`)** para notificar (`OnDeath`, `OnHealthChanged`, `OnPlayerSpawned`) | La UI, el audio y la cámara se enganchan sin acoplarse. Se suscriben en `OnEnable` y se desuscriben en `OnDisable`. |
| D8 | Movimiento **relativo a cámara** y cámara que sigue el `forward` del jugador | Controlador estándar de tercera persona. La cámara libre con ratón llega con P5. |
| D9 | Cero allocations en código caliente (`NonAlloc`, buffers prealocados, sin LINQ ni strings en loops) | Ver buenas prácticas en `features.md`. |
| D10 | **Animación dirigida por la FSM:** `PlayerAnimator` hace cross-fade al estado del Animator según el estado de la FSM; el controller casi no tiene transiciones. Root motion solo en las acciones de parkour (P22), y los estados de parkour terminan según el progreso de su clip | Evita duplicar la lógica de transiciones en dos sistemas que podrían desincronizarse. Fuera del parkour, el Rigidbody lo mueve el código (D1). |

## 7. Plan técnico para lo que falta — 📋 Propuesta

> Nada de esta sección está implementado. Las marcadas con **❓** siguen pendientes de aprobación
> (ver §8). Las marcadas con **✔** ya están aprobadas. Cuando algo se implemente, pasa a §5 y a `features.md`.

| Sistema | Diseño propuesto | Encaja con |
|---|---|---|
| **Input** ❓ | Crear `Assets/Input/AwakenedWarrior.inputactions` con el mapa `Gameplay` (Move, Look, Sprint, Jump, LightAttack, BlockHold, Dodge, Interact, Pause) usando los bindings del GDD §14 + gamepad, y asignar las referencias en `PlayerInputHandler`. El código **ya soporta** `InputActionReference` (también para Sprint), así que no hace falta reescribirlo. | Input System, doc oficial ("Using Actions" es el flujo recomendado). |
| **Regeneración de vida** | Componente `HealthRegen` que escucha `OnDamageReceived` y, tras N segundos sin daño, llama a `Heal` por tick (con timer, sin corrutina por frame). | GDD §5.11, D7. |
| **Caída mortal** | En `PlayerMovement`/`GroundChecker`, registrar la altura al despegar y llamar `InstantKill()` al aterrizar si la caída supera X m. Los barrancos pueden usar un trigger `KillZone`. | GDD §5.11, §5.12. |
| **Checkpoints / respawn** | `Checkpoint` (trigger, una activación) → `CheckpointManager` (en la escena) guarda la posición. Al `OnDeath` del Player: reposicionar con `Rigidbody.position` (teleport), `HealthSystem.InitializeHealth(100)` y reiniciar la FSM en `Idle`. | GDD §5.13, §17. |
| **Guardado** | `SaveSystem` estático: `JsonUtility` → `Application.persistentDataPath/save.json` con `{nivel, checkpointId}`. Una sola partida. | GDD §26, doc oficial de JsonUtility. |
| **Armas** | `WeaponHolder` ya está en el Player.prefab (desarmado 10/20). Falta crear 3 `WeaponData` (katana 20/35, yari 18/30 con más radio, kanabo 30/50) y un prefab `WeaponPickup` (trigger + E). Al recoger, se suelta la actual como pickup. | GDD §5.10, §18, D5. |
| **Enemigos** ✔ | Reescribir `Enemy` para 3D: **NavMeshAgent** (paquete ya instalado) con **Rigidbody kinemático**, como recomienda la documentación de AI Navigation. `NavMeshSurface` en cada nivel. Estados del GDD §21 (Idle, Detectar, Acercarse, Atacar, Defenderse, Buscar, Regresar) y una zona asignada (`EnemyZone`) de la que no salen. Tipos por `EnemyData`: `Archer` (distancia + flecha pooleada), `LightWarrior`, `HeavyWarrior`. La apariencia (modelos) la entrega el equipo en un paquete aparte. | GDD §5.14, §12, §21, D2, D5, D6. |
| **Jefes** | `Boss : Enemy` con estados extra (Analizar distancia, Reposicionarse, Bloquear, Esquivar), sin fases. `BossArena` cierra la salida con un trigger. | GDD §5.15, §13. |
| **Cámara** ✔ | Extender `CameraFollow` (sin Cinemachine) con yaw/pitch del ratón (`Look`), límites de pitch, cursor bloqueado en gameplay, shake por evento y encuadre de combate. El movimiento sigue siendo relativo a la cámara, pero la cámara deja de depender del `forward` del jugador. | GDD §15. |
| **Flujo de juego** | Escenas `MainMenu`, `World1_Level1`, `World1_Level2`, `World2_Level3`, cargadas con `SceneManager.LoadSceneAsync`. Pausa con `Time.timeScale = 0` y un panel de UI (uGUI). | GDD §17. |
| **Audio** | `AudioManager` con fuentes de audio pooleadas para SFX, que se suscribe a los eventos de combate. | GDD §23, D6, D7. |

## 8. Decisiones de alcance

| # | Tema | Decisión | Fecha | Estado |
|---|---|---|---|---|
| P1 | Controles | Adoptar los del GDD §14 (J/K/L/Q/E, Shift = sprint mantenido, Espacio = salto/vault). El slide pasa a **C** (solo mientras se esprinta). | 2026-09-29 / 30 | ✅ Implementada (J/K/L/Q, Shift, Espacio para salto y vault, C). Faltan E (sin sistema de armas) y ESC (sin pausa). |
| P2 | Parkour fuera del GDD | Wall jump fuera (desactivado el 29-sep, **eliminado** el 30-sep por P8). Ledge grab/climb y slide se conservan activos. | 2026-09-29 / 30 | ✅ Implementada |
| P3 | Sistema de XP | Eliminarlo: el juego no tiene XP. | 2026-09-29 | ✅ Implementada |
| P4 | Enemigos | Reescribir en 3D con NavMeshAgent. El equipo entrega el paquete de apariencia. | 2026-09-29 | ✔ Aprobada, 📋 por implementar |
| P5 | Cámara | Extender `CameraFollow` con control de ratón (sin Cinemachine). | 2026-09-29 | ✔ Aprobada, 📋 por implementar |
| P6 | Input | Migrar a un asset `.inputactions` propio. La plantilla por defecto ya se eliminó. | — | ❓ Pendiente: el equipo pidió primero una explicación |
| P7 | Estamina y HUD | Quedan fuera del desarrollo: el GDD no tiene estamina (§5.2) ni barras de vida (§16). Se eliminaron. | 2026-09-30 | ✅ Implementada |
| P8 | Código fuera del GDD | Lo que está fuera del GDD y no se usa se **elimina**, no se desactiva (wall jump, `Looter`/`Brute`, `initial_floor`, `InputSystem_Actions`). | 2026-09-30 | ✅ Implementada |
| P9 | Iluminación horneada | No se versiona: `Assets/Scenes/*/` en `.gitignore`, `LightingData.asset` marcado `binary`. | 2026-09-30 | ✅ Implementada |
| P10 | Identidad del producto | `productName` = Awakened Warrior, `companyName` = SUNUX GAMES. | 2026-09-30 | ✅ Implementada |
| P11 | Animaciones del jugador | Usar los clips Humanoid `LowPoly` del proyecto retargeteados a Ch45, más las transiciones de guardia Ch45 en el bloqueo. Solo esos 2 FBX Ch45 entran al proyecto; los otros 10 (caminatas, strafes, maletín, mandoble) quedan fuera. No se descarga nada. Hit y Death quedan para F19/F29. | 2026-09-30 | ✅ Implementada |
| P12 | Locomoción | WASD = correr (`BaseSpeed` 5) y Shift = sprint (+40 %). Actualizada el mismo día: la velocidad acelera y frena gradualmente y el blend pasa por Walk y Jog (clips del DPS); los controles y las velocidades finales no cambian. | 2026-09-30 | ✅ Implementada (T16) |
| P13 | Root motion | Todo el movimiento por código; Apply Root Motion desactivado. Estado de caída nuevo (`PlayerFallState`, resuelve T13). **Modificada por P22**: el parkour usa root motion. | 2026-09-30 | ✅ Implementada (salvo el parkour) |
| P14 | Alcance de la pasada "personaje jugable" | Animaciones de ataques, bloqueo real −70 % frontal, esquiva direccional con roll. Parkour solo con animaciones placeholder (el vault seguía automático; lo reemplazó P17). Color del personaje: extraer texturas embebidas y subir la luz direccional de 0.3 a 1. | 2026-09-30 | ✅ Implementada |
| P15 | Integración del Dynamic Parkour System | Adaptar su lógica y animaciones a nuestra FSM + Rigidbody. No se importan sus scripts, su cámara Cinemachine ni su input. Ver §5.12. | 2026-09-30 | ✅ Implementada |
| P16 | Caminar hacia atrás | Con S (y diagonales atrás) sin sprint el personaje retrocede mirando al frente a 1.5 m/s. Con sprint gira y esprinta normal. Clip: LowPoly *RunBackward* a ×0.6 (la caminata Ch45 resultó agachada). | 2026-09-30 | ✅ Implementada |
| P17 | Alcance del parkour | Vault con Espacio (GDD), slide con animación del DPS (P2), auto step y ledge con animaciones del DPS (P2). Fuera: escalar, postes, predicción de saltos, cornisa a cornisa, drops. | 2026-09-30 | ✅ Implementada |
| P18 | Combate más dinámico | Con los clips existentes: giro hacia el input e impulso hacia adelante al atacar, daño sincronizado con el golpe, buffer de input en el combo, cross-fades cortos. | 2026-09-30 | ✅ Implementada |
| P19 | Aterrizaje | Solo visual (Land / LandRun del DPS); no bloquea el control. **Reemplazada por P23** (aterrizaje según la altura, con recuperación). | 2026-09-30 | ⏸️ Reemplazada |
| P20 | Circuito de pruebas | Zona `ParkourTestCircuit` dentro de `Level-1` (losa propia) y `Spawner` movido a su entrada, porque el spawn original quedaba encerrado por la casa. **Reemplazada por P24.** | 2026-09-30 | ⏸️ Reemplazada |
| P21 | `Ground` con malla Capsule | Solo documentarlo (T19); no se cambia la malla. **Obsoleta:** el `Ground` se eliminó con P24. | 2026-09-30 | ⏸️ Obsoleta |
| P22 | Contacto físico del parkour | Root motion solo durante vault, agarre y subida, warpeado con `Animator.MatchTarget` hacia los puntos medidos, más IK de manos y pies (`PlayerContactIK`). Como hace el DPS; sin paquetes nuevos (ni Animation Rigging). | 2026-10-01 | ✅ Implementada |
| P23 | Movimiento humano y aterrizaje | Salto de ~1 m (`JumpSpeed` 4.5), inercia en el aire, aceleración 10 / frenado 13 m/s², giro más abierto a mayor velocidad, slide con fricción y sin impulso extra, step down, y aterrizaje según la altura: Fall → Landing → Recovery → Locomotion (absorbe velocidad y la devuelve en hasta 0.7 s, sin bloquear el control). | 2026-10-01 | ✅ Implementada |
| P24 | Parkour Test Area | `Level-1` pasa a ser el área de pruebas: suelo plano, perímetro y siete secciones sin textos. Se eliminan el blockout anterior (casas, `wall`, `Enemy` de la escena, `Ground` cápsula), los carteles y, como recursos sin uso, `HumanPlayer.prefab`, `LowPolyHumanAnimator.controller`, `ball.mat`, `metal.mat`, `Sign.mat` y los 10 FBX Ch45 de la raíz del repo. | 2026-10-01 | ✅ Implementada |

## 9. Deuda técnica y bugs conocidos

Solo se listan los abiertos. Los IDs no se reutilizan; los resueltos están en el historial de
`features.md` §5 (T1, T5, T6, T8–T12 se resolvieron el 2026-09-30; T4 y T13, también el 30-sep en
la integración del personaje; T14, en la integración del parkour; T7, T19, T20 y T21, el
2026-10-01 en la pasada de contacto físico).

| # | Problema | Dónde | Impacto |
|---|---|---|---|
| T2 | `Enemy.prefab` no tiene `Enemy.cs`, `Hitbox.targetLayers = 0`, y su vida (100) y daño (5) no son del GDD | `Prefabs/Enemy.prefab` | El prefab no tiene IA ni puede hacer daño. Se rehace con P4. |
| T3 | Enemigos 2.5D (freeze Z, eje X) | `Enemy.cs`, `EnemyGroundedStates.cs` | Incompatible con el jugador 3D. Se rehace con P4. |
| T15 | Licencia de las animaciones `LowPoly` sin confirmar | `Assets/LowPoly/` | Su origen ya se conoce: el `AssetOrigin` de los `.meta` indica el paquete gratuito *FREE Low Poly Human - RPG Character* de la Unity Asset Store (productId 219979). Falta confirmar sus términos en la página del paquete antes de publicar. |
| T16 | La velocidad de correr y esprintar no se midió contra el clip | `Player.prefab` → `PlayerMovement` | *Run* (DPS) y *Sprint* (LowPoly) son clips *in place*, sin velocidad medible; Walk y Jog sí se midieron. Si los pies patinan a 5 o 7 m/s, ajustar `BaseSpeed` o los umbrales del blend. |
| T17 | Animaciones provisionales | `PlayerAnimator.controller` | No hay patada (K usa un ataque de arma). Caminar hacia atrás usa un trote hacia atrás ralentizado. Las transiciones de guardia Ch45 se aceleran de ~1 s a 0.3 s. Solo hay un clip de vault (de una mano) y solo braced hang: si no hay muro bajo el borde, los pies quedan colgando. |
| T18 | Los FBX de las transiciones Ch45 incluyen la malla y las texturas | `Characters/Player/Animations/` | ~16 MB cada uno y el importador avisa de polígonos autointersectados de esa malla, que no se usa. Re-descargarlos de Mixamo "Without Skin" los reduciría a ~1 MB. |
| T22 | `ModelImporter.clipAnimations` falla en Unity 6000.6 | API de Editor | El getter registra `Cannot unmarshal intptr objects in structs` y devuelve los clips sin curvas; reescribirlos borra las curvas (así se perdió `LHandCurve`). **Regla:** editar los clips con `SerializedObject` sobre `m_ClipAnimations`, como `PlayerAnimationSetup`. |
| T23 | Las constantes del parkour dependen de los clips | `ParkourTimings.cs` | Los tiempos de contacto y los offsets se midieron muestreando los clips con una herramienta temporal (ya eliminada). Si se cambia un clip de vault, agarre o subida, hay que volver a medir; la prueba de Play Mode detecta el desajuste (manos lejos del borde, pies dentro de la geometría). |
