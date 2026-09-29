# Awakened Warrior — Features, historial y buenas prácticas

> Parte de la documentación del proyecto: [`contexto.md`](contexto.md) (qué es el juego) ·
> [`arquitectura.md`](arquitectura.md) (cómo está construido) · **`features.md`** (qué hay
> implementado, cómo se hizo y qué prácticas seguimos). Los estados (✅ 🟡 🔧 📋 ⬜ ⚠️) se definen
> en `contexto.md` §1.
>
> Estado verificado leyendo el código, los prefabs y la escena el 2026-09-29 (commit base
> `f09a2a3`). **Todavía no se ha verificado en Play Mode.** Los comportamientos se describen según
> el código.

---

## 1. Resumen de estado

| # | Feature | GDD | Estado |
|---|---|---|---|
| F01 | Movimiento en tercera persona | §5.1 | ✅ |
| F02 | Sprint | §5.2 | 🟡 automático, no Shift |
| F03 | Salto | §5.3 | ✅ |
| F04 | Vault | §5.4 | 🟡 automático, no Espacio |
| F05 | Ledge grab / climb | — | ⚠️ Fuera del GDD |
| F06 | Wall jump | — | ⏸️ Desactivado |
| F07 | Slide | — | ⚠️ Fuera del GDD |
| F08 | Esquivar | §5.5 | 🟡 sin cooldown efectivo, sin dirección |
| F09 | Ataque ligero | §5.6 | 🟡 |
| F10 | Ataque fuerte | §5.7 | 🟡 |
| F11 | Combo | §5.9 | 🟡 |
| F12 | Bloqueo | §5.8 | 🟡 no reduce daño |
| F13 | Vida, daño e i-frames | §5.11 | 🟡 |
| F14 | Regeneración de vida | §5.11 | ⬜ |
| F15 | Caída mortal | §5.11–5.12 | ⬜ |
| F16 | Sistema de armas | §5.10, §18 | 🟡 datos sin conectar |
| F17 | IA de enemigos | §5.14, §12, §21 | 🟡 2.5D, sin conectar |
| F18 | Jefes | §5.15, §13 | ⬜ |
| F19 | Checkpoints, muerte y reaparición | §5.13, §6 | ⬜ |
| F20 | Guardado | §26 | ⬜ |
| F21 | Cámara al hombro | §15 | 🟡 sin control de ratón ni shake |
| F22 | Input | §14 | 🟡 lectura directa, controles distintos |
| F23 | Animación | Pilar 2 | ⬜ |
| F24 | Spawning y object pooling | (técnico) | ✅ |
| F26 | Menú, pausa, flujo de escenas | §16–§17 | ⬜ |
| F27 | Niveles y mundos | §9–§10 | ⬜ solo blockout `Level-1` |
| F28 | Audio | §23 | ⬜ |
| F29 | Feedback de daño (sin HUD) | §16 | ⬜ |
| F30 | Herramientas de Editor | (técnico) | ✅ |

## 2. Diferencias GDD vs. implementación

Consulta esta tabla antes de tocar cualquier feature. El GDD manda; las decisiones de alcance
(P1–P6) están en `arquitectura.md` §8. Las filas marcadas con ✔ tienen una decisión aprobada que
todavía falta implementar.

| Tema | GDD final | Código actual |
|---|---|---|
| Nombre | Awakened Warrior | `warrior-woke` / `WarriorWoke` |
| Sprint ✔ P1 | Mantener Shift, +40 % | Automático tras 3 s corriendo; 8 → 11.5 (+43.75 %) |
| Shift ✔ P1 | Sprint | Slide |
| Vault ✔ P1 | Espacio cerca del obstáculo | Automático al correr contra un obstáculo bajo |
| Parkour | Solo salto, sprint y vault (§28) | Además: ledge grab/climb y slide (se conservan por decisión P2). Wall jump ⏸️ desactivado |
| Ataque ligero ✔ P1 | J | Clic izquierdo |
| Ataque fuerte ✔ P1 | K | Clic derecho |
| Bloqueo ✔ P1 | Mantener L, −70 %, solo frontal | Mantener F, **sin reducción real** (la constante dice 95 %) |
| Esquiva ✔ P1 | Q + dirección | E, siempre hacia `transform.forward` |
| Recoger arma ✔ P1 | E | No existe (E es esquivar) |
| Daño desarmado | Golpe 10 / patada 20 | Sin `WeaponHolder`: 10 / 10 (valor fijo del `Hitbox`). Con `WeaponHolder`: 8 / 20 |
| Combo | J → J → K, reinicio a los 0.5 s | Hasta 3 ligeros y cierre con pesado dentro de la ventana de 0.25–0.5 s |
| Regeneración | Sí | No |
| Enemigos ✔ P4 | Arquero, guerrero ligero, guerrero pesado; 3D; zona asignada | `Looter`, `Brute` (GDD anterior); 2.5D; patrulla en X |
| Cámara ✔ P5 | Control libre con ratón, shake, encuadre de combate | Sigue el `forward` del jugador; sin ratón ni shake |
| Arte | Realista, Japón Sengoku | Placeholder `LowPolyCity` (cartoon, "cyber") |

## 3. Fichas de features

Formato de cada ficha: **Objetivo · Estado · Archivos · Cómo funciona · Cómo se implementó ·
Dependencias · Consideraciones técnicas · Falta**.

### F01 — Movimiento en tercera persona ✅
- **Objetivo:** mover a Yukimura libremente en 3D con WASD, relativo a la cámara (GDD §5.1).
- **Archivos:** `Player/PlayerMovement.cs`, `Player/Player.cs`, `Player/PlayerInputHandler.cs`,
  `States/PlayerGroundedStates.cs` (`Idle`, `Run`).
- **Cómo funciona:** el input (−1..1 por eje) se proyecta sobre el forward y el right aplanados de
  la cámara para obtener `MoveDirection`. `Run` escribe `Rb.linearVelocity` =
  `MoveDirection × BaseSpeed (8)`, conservando Y. El personaje gira con `Mathf.SmoothDampAngle`
  (`turnSmoothTime` 0.12 s). `Idle` pone la velocidad horizontal en 0.
- **Cómo se implementó:** el proyecto nació 2.5D (Z congelado, un eje). En la sesión del 27-sep
  se migró a 3D: `RigidbodyConstraints.FreezeRotation`, `VerticalMove` en `IInputProvider` y
  `SetVelocity(Vector3, float)`. La rotación empezó con `RotateTowards` a 720°/s, pero la cámara
  giraba demasiado brusco, así que se cambió a `SmoothDampAngle`.
- **Dependencias:** `Camera.main` (si no hay, usa los ejes del mundo), `GroundChecker`.
- **Consideraciones:** la lógica de estados corre en el paso de física (50 Hz). La velocidad se
  asigna directamente, por lo que el Rigidbody no acelera.
- **Falta:** bloquear el movimiento al morir o durante animaciones de daño (GDD); animaciones de
  locomoción.

### F02 — Sprint 🟡
- **Objetivo:** +~40 % de velocidad manteniendo Shift; se cancela al recibir daño, bloquear o
  atacar (GDD §5.2).
- **Archivos:** `PlayerGroundedStates.cs` (`PlayerRunState`), `PlayerMovement.cs`
  (`SprintSpeed` 11.5, `SprintActivationTime` 3).
- **Cómo funciona hoy:** tras 3 s continuos en `Run`, `IsSprint = true`. Se apaga al salir de
  `Run`, al atacar, al bloquear o al esquivar. `Jump` hereda la velocidad de sprint.
- **Falta:** acción de sprint mantenido (Shift), `SprintSpeed = BaseSpeed × 1.4` y cancelarlo al
  recibir daño. Aprobado en P1, por implementar.

### F03 — Salto ✅
- **Archivos:** `PlayerAirStates.cs` (`PlayerJumpState`).
- **Cómo funciona:** solo desde el suelo (`IsGrounded`). `Enter` aplica `JumpSpeed` (7) en Y y
  conserva la velocidad horizontal. En el aire hay control total con `MoveDirection`. Al aterrizar
  (`vy ≤ 0` + suelo) pasa a `Idle`. También detecta cornisas (F05). La transición a wall jump
  está desactivada (F06).
- **Consideraciones:** no hay coyote time ni jump buffer. Un trigger de salto se consume en un solo
  tick de física.

### F04 — Vault 🟡
- **Objetivo:** pasar obstáculos bajos manteniendo el impulso, con Espacio (GDD §5.4).
- **Archivos:** `PlayerParkourStates.cs` (`PlayerVaultState`), `EnvironmentChecker.cs`
  (`IsObstacleVaultable`), `PlayerGroundedStates.cs` (transición desde `Run`).
- **Cómo funciona hoy:** en `Run`, si el raycast del centro golpea y el de la cabeza no (en
  `obstacleLayer` = Obstacle), entra a `Vault` **sin pulsar nada**. `Vault` pone el cuerpo
  kinemático y lo mueve con `Rb.MovePosition` en arco: 2 m hacia adelante, 1 m de altura, 0.4 s.
  Luego vuelve a `Run`.
- **Cómo se implementó:** originalmente con `Vector3.right`/`left` (2.5D). El 29-sep (commit
  `f09a2a3`) pasó a `transform.forward`, con pre-filtro `OverlapSphereNonAlloc`.
- **Consideraciones:** la distancia fija de 2 m no mide la profundidad del obstáculo, así que puede
  aterrizar dentro de uno profundo. Solo detecta objetos en layer `Obstacle`.
- **Falta:** requerir Espacio, validar la profundidad y altura del obstáculo y animación.

### F05 — Ledge grab / climb ⚠️ Fuera del GDD
- **Archivos:** `PlayerParkourStates.cs` (`LedgeGrab`, `LedgeClimb`), `EnvironmentChecker.IsLedgeDetected`.
- **Cómo funciona:** en `Jump`, si centro y cabeza tocan pared y un raycast hacia abajo encuentra
  la esquina, el cuerpo queda kinemático colgado (offset 0.4 atrás, 1 abajo). Espacio sube en
  0.5 s (primero vertical, luego horizontal). Presionar la dirección opuesta
  (`Dot < −0.5`) suelta.
- **Nota:** el GDD §28 limita el parkour a salto, sprint y vault, pero el equipo decidió
  **conservarlo activo** (P2).

### F06 — Wall jump ⏸️ Desactivado
- **Archivos:** `PlayerAirStates.cs` (`PlayerWallJumpState` y la transición comentada en
  `PlayerJumpState.LogicUpdate`).
- **Cómo funcionaba:** subiendo, tocando pared y pulsando Espacio (máx 2 seguidos, se reiniciaba
  al saltar desde el suelo), aplicaba `−forward × BaseSpeed` + `JumpSpeed`, con 0.15 s sin control.
- **Estado:** desactivado el 2026-09-29 (decisión P2, fuera del GDD). La clase del estado,
  `ConsecutiveWallJumps` y `EnvironmentChecker.IsTouchingWall` se conservan. Para reactivarlo, se
  descomenta la transición.

### F07 — Slide ⚠️ Fuera del GDD
- **Archivos:** `PlayerGroundedStates.cs` (`PlayerSlideState`).
- **Cómo funciona:** Shift en `Run` reduce el collider al 50 % y avanza a `SlideSpeed` 14 por
  0.7 s. No se levanta si hay techo (`HasCeilingOverhead`).
- **Nota:** se conserva activo (P2). Hoy usa Shift, que el GDD asigna al sprint; al aplicar P1
  necesita una tecla nueva (**por definir**).

### F08 — Esquivar 🟡
- **Objetivo:** Q + dirección, 0.5 s, 0.2 s invulnerable, cooldown 1 s. No durante un ataque ni
  una animación de daño (GDD §5.5).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerDodgeState`), `HealthSystem.ActivateIFrames`.
- **Cómo funciona hoy:** E desde `Idle` o `Run` (solo en el suelo). Dash a 12 u/s hacia
  `transform.forward` durante 0.5 s y llama `ActivateIFrames(0.2)`. Luego va a `Run` o `Idle`.
- **Problemas:** `CanDodge` (cooldown de 1 s) existe pero **ninguna transición lo consulta**. El
  cooldown es `static`, así que se compartiría entre instancias. La dirección no usa el input.
- **Falta:** aplicar el cooldown, esquivar en la dirección del input, tecla Q y animación.

### F09 — Ataque ligero 🟡
- **Objetivo:** golpe (J), daño 10 desarmado, hasta 3 encadenados, ~0.25 s (GDD §5.6).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerLightAttackState`), `Core/Combat/Hitbox.cs`,
  `Core/Combat/WeaponHolder.cs` (opcional).
- **Cómo funciona:** clic izquierdo. El personaje se planta (velocidad horizontal 0) y
  `Hitbox.Activate()` pega **en el primer tick** a los `IDamageable` dentro de la esfera (radio 0.6,
  layer Enemy). Dura 0.25 s y luego queda una ventana hasta 0.5 s para encadenar.
- **Consideraciones:** un clic dentro de los primeros 0.25 s **se pierde**, porque el trigger se
  consume en el tick siguiente y el estado todavía no escucha. Sin `WeaponHolder` en el prefab, el
  daño es el fijo del `Hitbox` (10).
- **Falta:** tecla J, buffer de input para el combo, sincronizar el hit con la animación.

### F10 — Ataque fuerte 🟡
- **Objetivo:** patada (K), daño 20 desarmado, 0.8 s, retroceso, vulnerable si falla (GDD §5.7).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerHeavyAttackState`).
- **Cómo funciona:** clic derecho. Se planta, activa la hitbox a los 0.1 s y dura 0.8 s sin
  cancelación.
- **Problemas:** sin `WeaponHolder` hace 10 (no 20). El knockback (`WeaponData.KnockbackForce`)
  no se aplica.
- **Falta:** tecla K, daño correcto, retroceso y animación.

### F11 — Combo 🟡
- **Objetivo:** J → J → K, reinicio si pasan más de 0.5 s (GDD §5.9).
- **Cómo funciona hoy:** `LightAttack` puede volver a entrar hasta 3 veces (`_chainCount`) o pasar
  a `HeavyAttack` si el input llega entre 0.25 s y 0.5 s desde el inicio del golpe. Permite
  J → K, J → J → K y J → J → J → K. El GDD define exactamente J → J → K.
- **Falta:** definir si se restringe a J → J → K, buffer de input y que con arma use las
  animaciones del arma.

### F12 — Bloqueo 🟡
- **Objetivo:** mantener L, −70 % de daño **solo frontal**, reduce la movilidad (GDD §5.8).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerBlockState`).
- **Cómo funciona hoy:** mantener F inmoviliza al personaje. Un clic izquierdo mientras bloquea
  contraataca (`LightAttack`). Expone `DamageReductionMultiplier = 0.05`, pero **nadie lo lee**, así
  que el daño recibido no cambia.
- **Falta:** conectar la reducción en `HealthSystem` (70 %), el chequeo frontal y la tecla L. El
  diseño propuesto está en `arquitectura.md` §7.

### F13 — Vida, daño e i-frames 🟡
- **Objetivo:** 100 HP, nunca fuera de [0, máx], 0.5 s de invulnerabilidad tras un golpe,
  feedback visual y sonoro (GDD §5.11).
- **Archivos:** `Core/Combat/HealthSystem.cs`, `Core/Interfaces/IDamageable.cs`, `Hitbox.cs`.
- **Cómo funciona:** `TakeDamage(amount, source)` ignora el daño si está muerto o en i-frames,
  aplica clamp y dispara `OnDamageReceived`, `OnHealthChanged` y `OnDeath`. Los i-frames se miden con
  `Time.time`, sin corrutinas. `ActivateIFrames(d)` desplaza la marca de tiempo.
- **Consideraciones:** está en el Player.prefab y en el Enemy.prefab. **Nadie escucha `OnDeath`
  del jugador**, así que morir no tiene efecto.
- **Falta:** regeneración (F14), muerte y reaparición (F19), feedback (F29).

### F14 — Regeneración de vida ⬜
- **Objetivo:** regenerar automáticamente tras unos segundos sin daño (GDD §5.11).
- **Plan:** componente `HealthRegen` (ver `arquitectura.md` §7). El GDD no define el tiempo ni la
  tasa; hay que acordarlos en equipo.

### F15 — Caída mortal ⬜
- **Objetivo:** muerte instantánea al caer desde gran altura o en un barranco (GDD §5.11, §5.12).
- **Plan:** medir la altura de la caída + trigger `KillZone` que llama `HealthSystem.InstantKill()`
  (el método ya existe). El GDD no define la altura; hay que acordarla.

### F16 — Sistema de armas 🟡
- **Objetivo:** recoger con E katana (20/35), yari (18/30, más alcance) o kanabo (30/50, más
  lento). Una a la vez; al recoger otra se suelta la actual (GDD §5.10, §18).
- **Archivos:** `Core/Combat/WeaponData.cs` (SO), `Core/Combat/WeaponHolder.cs`.
- **Cómo funciona hoy:** `WeaponHolder.Equip(data)` guarda el arma, ajusta el radio del `Hitbox` y
  dispara `OnWeaponChanged`. Los estados de ataque piden el daño a `WeaponHolder`.
- **Problemas:** **no hay assets `WeaponData`**, `WeaponHolder` no está en el Player.prefab y no
  existe pickup, drop, modelo en la mano ni diferencia de velocidad (kanabo más lento). Los valores
  desarmados (8/20) no coinciden con el GDD (10/20).
- **Falta:** todo lo anterior. Plan en `arquitectura.md` §7.

### F17 — IA de enemigos 🟡
- **Objetivo:** arquero (50 HP, flechas de 10, distancia), guerrero ligero (80, cortes de 12,
  rápido, esquiva) y guerrero pesado (150, golpes de 25, lento, bloquea, embiste). FSM
  `Idle → Detectar → Acercarse → Atacar → Defenderse → Buscar → Regresar`, sin salir de su zona
  (GDD §5.14, §12, §21).
- **Archivos:** `Enemy/Enemy.cs`, `Enemy/StateMachine/*`, `Enemy/Types/{Looter,Brute}.cs`,
  `Core/Combat/EnemyData.cs`.
- **Cómo funciona el código:** estados `Patrol → Chase → Attack → Dead`. La detección es una esfera
  de `DetectionRange` + tag `Player` + línea de visión, y la pérdida del objetivo es por
  `LoseTrackRange`. El ataque para al enemigo, activa la hitbox al 30 % de `AttackDuration` y respeta
  `AttackCooldown`. Al morir espera 1.5 s y vuelve al pool.
- **Cómo se implementó:** commit `796fac3` (29-sep), con la misma arquitectura que el jugador.
- **Problemas:** movimiento **2.5D** (Z congelado, eje X), el `Enemy.prefab` no tiene el script, el
  tag del prefab es `Player`, faltan los estados Defenderse, Buscar y Regresar, no hay ataque a
  distancia y los tipos son del GDD anterior.
- **Falta:** reescritura 3D con NavMeshAgent (aprobada en P4; la apariencia llega en un paquete del
  equipo). Plan en `arquitectura.md` §7.

### F18 — Jefes ⬜
- Líder del clan rival (300 HP, katana, 15–25) y el Comandante (450 HP, katana, 20–30). Sin fases.
  La arena bloquea la salida (GDD §5.15, §13). Plan: `Boss : Enemy` + `BossArena`.

### F19 — Checkpoints, muerte y reaparición ⬜
- Checkpoint automático al cruzarlo (una activación). Al morir, reaparecer en el último con la vida
  completa (GDD §5.13, §6). Plan: `Checkpoint` + `CheckpointManager` que escucha `OnDeath`.

### F20 — Guardado ⬜
- Una partida: nivel alcanzado + último checkpoint. Autoguardado al activar un checkpoint y al
  completar un nivel (GDD §26). Plan: `JsonUtility` en `Application.persistentDataPath`.

### F21 — Cámara al hombro 🟡
- **Objetivo:** cámara estilo Sleeping Dogs con control libre, zoom ligero, shake y encuadre de
  combate (GDD §15).
- **Archivos:** `Camera/CameraFollow.cs`, prefab `Main Camera`.
- **Cómo funciona:** en `LateUpdate` se coloca detrás del hombro del jugador (`shoulderHeight`
  1.6, `shoulderSide` 0.5, `distance` 3.5), mira hacia adelante (`lookAheadDistance` 2), suaviza con
  `SmoothDamp`/`Slerp` y usa un `SphereCast` para no atravesar paredes. Encuentra al Player por
  evento, sin referencias manuales.
- **Cómo se implementó:** se reescribió en la sesión del 27-sep a partir de una cámara lateral 2.5D.
- **Falta:** yaw y pitch con ratón o stick (el GDD dice que el jugador mueve la cámara), shake,
  encuadre de combate y zoom por contexto. Aprobado en P5: extender `CameraFollow` con ratón. Revisar posible jitter (arquitectura T7).

### F22 — Input 🟡
- **Archivos:** `Player/PlayerInputHandler.cs`, `Core/Interfaces/IInputProvider.cs`.
- **Cómo funciona:** lee `Keyboard.current`/`Mouse.current` en `Update` y bufferiza los triggers.
  Hay `InputActionReference` opcionales, todos vacíos. "Last input wins" si se presionan direcciones
  opuestas.
- **Falta:** un asset `.inputactions` con los bindings del GDD + gamepad (P1, P6) y las acciones
  Sprint, Interact (E), Look y Pause.

### F23 — Animación ⬜
- Hay clips `LowPoly` y un `LowPolyHumanAnimator.controller` con solo `Idle`. El `character.fbx`
  se importa como Humanoid si el rig lo permite, pero nada está conectado. Los estados tienen
  `// TODO: trigger animation`. El GDD lo pone como pilar (2) y el riesgo §28 pide reutilizar
  animaciones.

### F24 — Spawning y object pooling ✅
- **Archivos:** `Core/Spawning/{ObjectPoolManager,Spawner,ReturnToPoolDelay}.cs`,
  `Core/Interfaces/IPoolable.cs`, prefabs `GameManager` y `Spawner`.
- **Cómo funciona:** ver `arquitectura.md` §5.6. En `Level-1`, el Player nace del pool `player`
  gracias al `Spawner` (OnStart).
- **Cómo se implementó:** commit `4d9222d` (18-sep). El 27-sep se bajó el `Spawner` de Y = 40 a
  Y = 2 y se agregó el `Ground`, porque el jugador aparecía en el aire sin suelo.
- **Consideraciones:** el pool crece si se vacía (usa `Instantiate` en runtime). Hay que
  dimensionar `initialSize` para que eso no ocurra en combate.

### F26 — Menú, pausa y flujo de escenas ⬜
- Splash → menú (Nueva partida, Continuar, Salir) → gameplay → pausa (Continuar, Reiniciar desde
  checkpoint, Menú principal) → siguiente nivel → final (GDD §17).

### F27 — Niveles y mundos ⬜
- Solo existe `Level-1.unity` como blockout: suelo de 100×100, un muro de ProBuilder y dos casas
  `LowPolyCity`. Faltan los 3 niveles del GDD (§10).

### F28 — Audio ⬜
- Música tradicional japonesa, SFX de acciones y ambiente, sin voces (GDD §23).

### F29 — Feedback de daño (sin HUD) ⬜
- Sin barras de vida. La salud se comunica con el estado y las animaciones del personaje, más VFX,
  SFX y camera shake (GDD §16). Se engancharía a `HealthSystem.OnDamageReceived`/`OnHealthChanged`.

### F30 — Herramientas de Editor ✅
- `SceneAutoLoader` y `PlayerCharacterSetup`: ver `arquitectura.md` §5.9 y `contexto.md` §11.

---

## 4. Buenas prácticas de Unity 3D para este proyecto

Reglas obligatorias para el código nuevo, basadas en la documentación oficial de Unity 6 (fuentes
al final) y en el manual interno de optimización que ya tenía el equipo. Cada regla dice **dónde
aplica en nuestro proyecto**.

### 4.1 Ciclo de vida y orden de ejecución

| Método | Úsalo para | En nuestro proyecto |
|---|---|---|
| `Awake` | Cachear componentes propios e inicializar estado interno | `PlayerMovement.CacheComponents`, `BuildStateMachine` |
| `OnEnable` / `OnDisable` | Suscribirse y desuscribirse de eventos; reinicio al salir del pool | `Enemy` ↔ `HealthSystem.OnDeath`, `CameraFollow` ↔ `Player.OnPlayerSpawned` |
| `Start` | Lógica que necesita que **otros** objetos ya existan | `StateMachine.Initialize(IdleState)` |
| `Update` | Leer input y lógica por frame que no toca física | `PlayerInputHandler` |
| `FixedUpdate` | Todo lo que escribe en el Rigidbody (velocidad, `MovePosition`) | `PlayerMovement`, `Enemy` |
| `LateUpdate` | Cámaras y seguimiento después del movimiento | `CameraFollow` |

- **El orden entre GameObjects distintos no está garantizado** (Unity: *"you can't rely on the
  order in which the same event function is invoked for different GameObjects"*). Nunca asumas
  que el `Awake` de otro objeto ya corrió. Usa eventos (como `OnPlayerSpawned`), `Start`, o
  *Script Execution Order* si no hay alternativa.
- `FixedUpdate` puede correr 0, 1 o varias veces por frame. **El input de un solo frame
  (`wasPressedThisFrame`) se bufferiza en `Update` y se consume en `FixedUpdate`**, como ya hace
  `IInputProvider.Consume*()`. No leas `wasPressedThisFrame` directamente en `FixedUpdate`.
- Si un objeto se suscribe a un evento estático (`Player.OnPlayerSpawned`), **siempre** debe
  desuscribirse en `OnDisable`. Si no, deja fugas y llamadas a objetos destruidos.

### 4.2 Física (Rigidbody)

- Mueve un Rigidbody **solo** con su API (`linearVelocity`, `AddForce`, `MovePosition`,
  `position`) y **dentro de `FixedUpdate`**. Nunca con `transform.position` sobre un cuerpo con
  física. En el jugador, todo pasa por los helpers de `PlayerMovement`.
- `Rb.MovePosition` es para cuerpos **kinemáticos** y respeta la interpolación (Vault y Ledge ya
  lo usan). Para **teletransportar** (reaparición en checkpoint), usa `Rb.position`, no
  `MovePosition`.
- **Interpolación:** según Unity, activarla *"only if you see jitter"*. Con la cámara en
  `LateUpdate` siguiendo un cuerpo que se mueve a 50 Hz, lo probable es que haya jitter (T7).
  Verifícalo y, si aparece, pon `Interpolate` en el Rigidbody del Player. Con interpolación activa,
  cualquier cambio directo al transform necesita `Physics.SyncTransforms`.
- **Colliders primitivos** (esfera > cápsula > caja > mesh convexo > mesh cóncavo, en ese orden de
  costo). Personajes con `CapsuleCollider`; props y edificios de nivel con cajas o compuestos
  cuando se pueda. Evita Mesh Colliders no convexos en objetos que se mueven.
- **Layers + Layer Collision Matrix** (Project Settings → Physics) para que solo colisione lo que
  debe. Toda query usa un `LayerMask` explícito (`Ground`, `Obstacle`, `Player`, `Enemy`). Nunca
  uses `~0` en queries calientes. Hoy `Enemy.DetectPlayer` hace el overlap sin máscara; al
  reescribirlo, filtra por la layer `Player`.
- **Queries sin garbage:** `Physics.OverlapSphereNonAlloc` / `RaycastNonAlloc` con buffers
  prealocados (vigentes y soportados en Unity 6). Ojo: *"Does not attempt to grow the buffer"*,
  así que dimensiona el buffer para el peor caso (el `Hitbox` usa 10) y pasa
  `QueryTriggerInteraction` explícito cuando los triggers no deban contar.
- **Pre-filtros espaciales** antes de varios raycasts: un `OverlapSphere` barato primero, como hace
  `EnvironmentChecker`. Nada de comprobaciones de todos contra todos (O(n²)).
- Fixed Timestep = 0.02 s. No lo cambies sin medir con el Profiler.

### 4.3 Input System

- Flujo oficial recomendado: **Actions** (asset `.inputactions` / project-wide actions)
  referenciadas y leídas en código. Leer dispositivos directamente (lo que hacemos hoy) es, según la
  documentación, *"a less flexible workflow"* adecuado para *"fast prototyping"*. Al migrar (P6)
  hay que mantener `IInputProvider` para que el resto del código no cambie.
- El proyecto tiene `activeInputHandler = Input System`: **no uses `UnityEngine.Input`
  (legacy)**, porque lanza excepciones.
- Habilita las acciones en `OnEnable` y deshabilítalas en `OnDisable` (ya se hace).

### 4.4 Máquinas de estados (jugador, enemigos, jefes)

- Una clase por estado con `Enter`, `LogicUpdate`, `PhysicsUpdate` y `Exit`. Las transiciones se
  deciden **dentro** del estado. La máquina (`*StateMachine`) queda genérica.
- Instancia los estados **una vez** en `Awake` (ya se hace). No crees estados con `new` en cada
  transición.
- Cachea los componentes que use un estado en su constructor (como `Hitbox` en `LightAttack`),
  nunca en `LogicUpdate`.
- Los timers se miden con `Time.time - startTime`, sin corrutinas por estado. Guarda los
  cooldowns como **campo de instancia**, no `static` (el `static` de `PlayerDodgeState` se
  compartiría entre instancias).
- Los estados no llaman al `Animator` directamente. Un componente de presentación
  (`PlayerAnimator`) traduce el estado a parámetros. Así las animaciones se pueden cambiar sin
  tocar la lógica.

### 4.5 Datos: ScriptableObjects

- Los datos de diseño van en SO (`WeaponData`, `EnemyData`, y en el futuro los tiempos de
  regeneración y la altura de caída mortal), no hardcodeados en constantes. Así se cumplen los
  valores del GDD sin recompilar y *"reduce … memory usage by avoiding copies of values"*.
- **Los SO son de solo lectura en runtime.** En un build *"you can only read saved data from the
  ScriptableObject assets"*, y en el Editor los cambios por script ensucian el asset. El estado
  mutable (vida actual, arma equipada) vive en componentes, nunca en el SO. El guardado de partida
  va a JSON, no a SO.
- Los valores iniciales de los SO deben coincidir con las tablas del GDD (`contexto.md` §4–§5).

### 4.6 Memoria y garbage collector

- **Pooling** para todo lo que nace y muere seguido (enemigos, flechas, VFX, SFX, pickups
  soltados). Tenemos `ObjectPoolManager`; para pools internos pequeños también sirve
  `UnityEngine.Pool.ObjectPool<T>` (callbacks, `collectionCheck`, `maxSize`). Nada de
  `Instantiate` ni `Destroy` en el game loop.
- En código que corre cada tick: nada de LINQ, lambdas que capturen variables, concatenación de
  strings, `new` de arrays o listas ni boxing. `Debug.Log` con interpolación también genera
  garbage; úsalo solo en eventos puntuales (level up, equip), nunca por frame.
- Cachea todo lookup (`GetComponent`, `Find*`, `Camera.main`) en `Awake`/`Start`.
  `CompareTag` en lugar de `tag ==`. Parámetros del Animator con `Animator.StringToHash` en
  campos `static readonly`.
- Cachea los `WaitForSeconds` si se usan corrutinas (ya se hace en `Spawner` y
  `ReturnToPoolDelay`).

### 4.7 Prefabs y escenas

- Todo lo que aparece más de una vez o se configura por código es un **prefab**. Usa **Prefab
  Variants** para variantes de un mismo concepto (p. ej. `Enemy` base → `Archer`, `LightWarrior`,
  `HeavyWarrior`; `WeaponPickup` → katana/yari/kanabo). Usa prefabs anidados para componer.
- Los ajustes se hacen en el prefab, no con overrides sueltos en la escena, salvo la posición o
  datos propios de esa instancia.
- Comprueba tag y layer de cada prefab (hoy `Enemy.prefab` tiene tag `Player`, error T1).
- Cambios de escena con `SceneManager.LoadSceneAsync` (no bloquea el hilo principal). Si hace falta
  UI persistente, usa carga aditiva.
- Dos personas no editan la misma escena al mismo tiempo. Los niveles pueden partirse en escenas
  aditivas por zona si crecen.

### 4.8 Navegación (enemigos)

- `NavMeshSurface` en cada nivel, horneado desde la geometría. Vuelve a hornear al cambiar el nivel.
- `NavMeshAgent` + `Rigidbody` **kinemático**: la documentación advierte que usarlos juntos sin
  kinemático crea una carrera entre los dos sistemas. No combines `NavMeshAgent` y `NavMeshObstacle`
  en el mismo objeto (*"the agent trying to avoid itself"*). Al morir, desactiva el agente.
- La "zona asignada" del GDD se modela con NavMesh Areas/Modifiers o un volumen `EnemyZone` que
  limite los destinos.

### 4.9 Rendimiento y profiling

- **Mide antes de optimizar:** Unity Profiler (CPU/GPU, GC Alloc por frame), Frame Debugger (draw
  calls) y Profile Analyzer (ya instalado). Mide en **ms por frame**, no en FPS, y en un build
  Release, no solo en el Editor.
- Primero identifica si el cuello de botella es la CPU o la GPU (si bajar la resolución sube los
  FPS, es la GPU).
- GPU/URP: static batching para la geometría fija del nivel, GPU instancing o SRP Batcher para
  props repetidos, iluminación horneada para lo estático, sombras dinámicas con distancia y
  resolución moderadas, LOD en props de nivel y occlusion culling en la capital (Mundo 2).
  Aplica cada post-proceso uno a uno midiendo su costo.
- Texturas del tamaño que se necesita, con mipmaps y compresión por plataforma.

### 4.10 Organización y control de versiones

- Scripts en `Assets/scripts/<Dominio>/` (ver `arquitectura.md` §2). Assets de datos en
  `Assets/Data/`. Un tipo público por archivo, salvo los grupos de estados que ya existen.
- Serialización **Force Text** + `UnityYAMLMerge` (ver `contexto.md` §12). Nunca toques `.meta` a
  mano.
- Campos de Inspector: `[SerializeField] private` + propiedad de solo lectura, con `[Header]` y
  `[Tooltip]`, como en el código existente.
- Commits en Conventional Commits, pequeños y de un solo tema.

### 4.11 Checklist por cambio

1. ¿Leí `contexto.md`, `arquitectura.md` y `features.md`, el código relacionado y el GDD si aplica?
2. ¿El cambio respeta el GDD y el MVP? ¿Contradice alguna decisión pendiente (§8 de arquitectura)?
3. ¿Esto corre cada frame o tick? ¿Genera garbage? ¿Está cacheado? ¿Se puede poolear?
4. ¿La física se toca solo en `FixedUpdate` y a través del Rigidbody?
5. ¿Las queries tienen `LayerMask` y buffers NonAlloc?
6. ¿Me suscribí a eventos en `OnEnable` y me desuscribí en `OnDisable`?
7. ¿Los datos de diseño están en un SO con los valores del GDD?
8. ¿Lo probé en Play Mode? ¿Revisé la consola y el Profiler si es un sistema caliente?
9. ¿Actualicé los tres documentos y verifiqué que no se contradicen?

### Fuentes oficiales consultadas (Unity 6)

- Event function execution order — docs.unity3d.com/6000.0/Documentation/Manual/execution-order.html
- Rigidbody interpolation — docs.unity3d.com/6000.2/Documentation/Manual/rigidbody-interpolation.html
- Rigidbody.MovePosition — docs.unity3d.com/6000.2/Documentation/ScriptReference/Rigidbody.MovePosition.html
- Physics.OverlapSphereNonAlloc — docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.OverlapSphereNonAlloc.html
- Collider types and performance — docs.unity3d.com/6000.0/Documentation/Manual/physics-optimization-cpu-collider-types.html
- Layer-based collision — docs.unity3d.com/6000.0/Documentation/Manual/LayerBasedCollision.html
- Input System workflows — docs.unity3d.com/Packages/com.unity.inputsystem@1.14/manual/Workflows.html
- ScriptableObject — docs.unity3d.com/6000.0/Documentation/Manual/class-ScriptableObject.html
- ObjectPool&lt;T&gt; — docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.ObjectPool_1.html
- Managed memory optimization — docs.unity3d.com/6000.0/Documentation/Manual/performance-optimizing-code-managed-memory.html
- Prefabs — docs.unity3d.com/6000.0/Documentation/Manual/Prefabs.html
- SceneManager.LoadSceneAsync — docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html
- Animator.StringToHash — docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator.StringToHash.html
- AI Navigation 2.0 (NavMesh, Agent, mezcla con Rigidbody) — docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/
- JsonUtility — docs.unity3d.com/6000.0/Documentation/Manual/json-serialization.html
- Cinemachine 3 ThirdPersonFollow — docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineThirdPersonFollow.html
- Guías oficiales de buenas prácticas (perfilado, C# style guide, arquitectura con SO, patrones y SOLID) — docs.unity3d.com/6000.0/Documentation/Manual/best-practice-guides.html

---

## 5. Historial de implementación

Registro cronológico de **qué se implementó y cómo**. Agrega una entrada por cada cambio, con el
problema, la causa, la solución y el estado (formato del GDD §29 para bugs).

| Fecha | Commit | Autor | Qué se hizo |
|---|---|---|---|
| 2026-09-12 | `e6a3c1f`, `2891bca`, `ac4127b` | Alan | Proyecto inicial, movimiento básico 2.5D, input, cámara lateral, blockout del nivel 1. |
| 2026-09-18 | `12890d5`…`e321e37` | Alan | Limpieza de paquetes, object pooling + spawner, assets de `LowPolyCity`, URP y ajustes de calidad, Profile Analyzer. |
| 2026-09-19/20 | `2d10078`, `4902543`, `c434090` | Alan | Máquina de estados del jugador. Vault y ledge con `rb.MovePosition`. Wall jump restringido con cooldown. |
| 2026-09-26 | `45cbab1`, `189c623` | Alan | Estados de combate, prefabs Player/Enemy, escena `Level-1`. |
| 2026-09-27 | `66e2aed` | Axel | Sesión de repo, cámara y personaje (detalle abajo). |
| 2026-09-29 | `796fac3` | Alan | `HealthSystem.ActivateIFrames`, `WeaponData`/`WeaponHolder`, `EnemyData`, FSM de enemigos, `Looter`/`Brute`, `PlayerXpSystem`. |
| 2026-09-29 | `f09a2a3` | Alan | Parkour reactivado en 3D (`EnvironmentChecker` con `Vector3` + pre-filtro `OverlapSphereNonAlloc`) y reglas de buenas prácticas en la documentación. |
| 2026-09-29 | `ccc58ae` | Axel | Se incorpora el GDD final. La documentación se consolida en `docs/contexto.md`, `docs/arquitectura.md` y `docs/features.md`. Se audita el código contra el GDD. Sin cambios de gameplay. |

| 2026-09-29 | (este commit) | Axel | Decisiones P2 y P3: wall jump desactivado (transición comentada, código conservado) y sistema de XP eliminado (`PlayerXpSystem`, `IXpReceiver`, `EnemyData.xpReward`, la llamada en `Enemy.OnDeath` y comentarios de recompensa en `Looter`/`Brute`). Compilación verificada en batch mode sin errores. |

### Detalle — sesión del 2026-09-27 (repo, cámara y personaje)

1. **"Hago pull y no veo los cambios de mi compañero".**
   - **Causa:** Unity recuerda la escena abierta en `Library/` (ignorada por git), y existe una
     escena señuelo (la demo del asset pack).
   - **Solución:** `SceneAutoLoader` (abre `Level-1` una vez por sesión) + documentación.
   - **Bug propio:** la primera versión usaba `EditorApplication.isBatchMode` (no existe). Lo
     correcto es `Application.isBatchMode`. El error rompía la compilación de todo el proyecto hasta
     que se corrigió.
2. **Higiene de git:** `.gitattributes` nuevo (LF, `UnityYAMLMerge`, binarios), merge driver
   registrado localmente y Git LFS evaluado pero no activado (lejos del límite de 100 MB, y
   obligaría a todo el equipo a instalarlo).
3. **Migración de 2.5D a 3D libre + cámara al hombro:**
   - Se confirmó el alcance con el equipo antes de tocar código.
   - `VerticalMove` en `IInputProvider`, movimiento relativo a cámara, `FreezeRotation` en lugar
     de `FreezePositionZ`, sobrecarga `SetVelocity(Vector3, float)`.
   - La rotación pasó de `RotateTowards` a `SmoothDampAngle` porque la cámara giraba demasiado
     rápido.
   - `CameraFollow` se reescribió como cámara al hombro con `SphereCast`.
   - Slide y Dodge pasaron a `transform.forward`, y las condiciones de input a `HasMoveInput`.
   - El parkour quedó comentado temporalmente; el 29-sep se reactivó en 3D (`f09a2a3`).
4. **Pipeline del modelo del personaje:** `PlayerCharacterSetup` (Humanoid o Generic, reemplazo
   del placeholder, reescalado a ~1.8 m, collider y `HeadPoint` ajustados, idempotente). Se hizo en
   el Editor y no editando YAML, porque el Avatar y los bounds solo los calcula el importador de
   Unity. Modelo: `Assets/Characters/Player/character.fbx` (antes `Protagonista.fbx`).
5. **Suelo y punto de spawn:** el `Spawner` estaba en `(0, 40, −2.5)` sin suelo debajo. Se agregó
   `Ground` (100×100, top en Y = 0, layer Ground, `floors.mat`) y se bajó el `Spawner` a Y = 2.
   `initial_floor` (blockout viejo) nunca se colocó en esta escena.
