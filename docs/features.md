# Awakened Warrior — Features, historial y buenas prácticas

> Parte de la documentación del proyecto: [`contexto.md`](contexto.md) (qué es el juego) ·
> [`arquitectura.md`](arquitectura.md) (cómo está construido) · **`features.md`** (qué hay
> implementado, cómo se hizo y qué prácticas seguimos). Los estados (✅ 🟡 🔧 📋 ⬜ ⚠️ ⏸️) se
> definen en `contexto.md` §1.
>
> Estado verificado el 2026-09-30, después de la integración del parkour (ver §5): los scripts
> compilan sin errores, `PlayerAnimationSetup` valida Avatars, material, clips, Missing Scripts y
> poses en batch mode, y **`ParkourPlayModeTest` lo prueba en Play Mode real** con teclado simulado:
> 36/36 comprobaciones (F32). Lo que la prueba no cubre (cómo se ve y se siente) falta revisarlo
> jugando.

---

## 1. Resumen de estado

| # | Feature | GDD | Estado |
|---|---|---|---|
| F01 | Movimiento en tercera persona | §5.1 | ✅ |
| F02 | Sprint | §5.2 | ✅ |
| F03 | Salto y caída | §5.3 | ✅ |
| F04 | Vault | §5.4 | ✅ |
| F05 | Ledge grab / climb | — | ⚠️ Fuera del GDD (se conserva, P2) |
| F07 | Slide | — | ⚠️ Fuera del GDD (se conserva, P2; tecla C) |
| F08 | Esquivar | §5.5 | ✅ |
| F09 | Ataque ligero | §5.6 | ✅ |
| F10 | Ataque fuerte | §5.7 | 🟡 sin retroceso, animación placeholder |
| F11 | Combo | §5.9 | 🟡 |
| F12 | Bloqueo | §5.8 | ✅ |
| F13 | Vida, daño e i-frames | §5.11 | 🟡 |
| F14 | Regeneración de vida | §5.11 | ⬜ |
| F15 | Caída mortal | §5.11–5.12 | ⬜ |
| F16 | Sistema de armas | §5.10, §18 | 🟡 `WeaponHolder` conectado, sin armas ni pickup |
| F17 | IA de enemigos | §5.14, §12, §21 | 🟡 2.5D, sin conectar |
| F18 | Jefes | §5.15, §13 | ⬜ |
| F19 | Checkpoints, muerte y reaparición | §5.13, §6 | ⬜ |
| F20 | Guardado | §26 | ⬜ |
| F21 | Cámara al hombro | §15 | 🟡 sin control de ratón ni shake |
| F22 | Input | §14 | 🟡 lectura directa, sin gamepad |
| F23 | Animación | Pilar 2 | 🟡 clips provisionales |
| F24 | Spawning y object pooling | (técnico) | ✅ |
| F26 | Menú, pausa, flujo de escenas | §16–§17 | ⬜ |
| F27 | Niveles y mundos | §9–§10 | ⬜ solo blockout `Level-1` |
| F28 | Audio | §23 | ⬜ |
| F29 | Feedback de daño (sin HUD) | §16 | ⬜ |
| F30 | Herramientas de Editor | (técnico) | ✅ |
| F31 | Auto step | (técnico) | ✅ |
| F32 | Circuito de parkour y pruebas automáticas | (técnico) | ✅ |

IDs eliminados (no se reutilizan): **F06** wall jump (eliminado el 2026-09-30, P2/P8) y **F25**
sistema de XP (eliminado el 2026-09-29, P3). Estamina y HUD nunca tuvieron ficha y también se
eliminaron (P7). Ver `arquitectura.md` §5.8.

## 2. Diferencias GDD vs. implementación

Consulta esta tabla antes de tocar cualquier feature. El GDD manda; las decisiones de alcance
(P1–P10) están en `arquitectura.md` §8. Las filas marcadas con ✔ tienen una decisión aprobada que
todavía falta implementar.

| Tema | GDD final | Código actual |
|---|---|---|
| Nombre | Awakened Warrior | `warrior-woke` / `WarriorWoke` |
| Caminar / correr | "Caminar / correr" con WASD | WASD = correr a 5 m/s, acelerando y frenando de forma gradual (el blend pasa por caminar y trotar) (P12) |
| Caminar hacia atrás | No lo define | S sin sprint retrocede mirando al frente a 1.5 m/s (P16) |
| Parkour | Solo salto, sprint y vault (§28) | Además: ledge grab/climb y slide (se conservan por decisión P2; slide con C mientras se esprinta) y auto step (movimiento base). El wall jump se eliminó |
| Ataque fuerte | Patada (desarmado) | Animación de ataque con arma de una mano como placeholder (no hay clip de patada) |
| Recoger arma ✔ P1 | E | No existe (E no hace nada todavía) |
| Pausa | ESC | No existe |
| Combo | J → J → K, reinicio a los 0.5 s | Hasta 3 ligeros y cierre con pesado dentro de la ventana de 0.25–0.5 s |
| Regeneración | Sí | No |
| Enemigos ✔ P4 | Arquero, guerrero ligero, guerrero pesado; 3D; zona asignada | Un `Enemy` genérico sin tipos del GDD; 2.5D; patrulla en X |
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
  la cámara para obtener `MoveDirection`. `Run` lleva la velocidad horizontal hacia
  `MoveDirection × BaseSpeed (5)` con `AccelerateHorizontal` (12 m/s² al acelerar, 16 m/s² al
  frenar), conservando Y; `Idle` frena hasta 0 con la misma desaceleración. Así el blend
  `Locomotion` pasa por Walk y Jog al arrancar y al detenerse. El personaje gira con
  `Mathf.SmoothDampAngle` (`turnSmoothTime` 0.12 s).
- **Caminar hacia atrás (P16):** con input hacia atrás (S o diagonales) y sin sprint,
  `IsBackpedaling` es verdadero: `Run` usa `BackpedalSpeed` (1.5 m/s) y el cuerpo mira al frente de
  la cámara en lugar de girar, así que la cámara no da media vuelta. `PlayerAnimator` manda `Speed`
  negativa y el blend reproduce *RunBackward* (LowPoly) a ×0.6. Con Shift gira y esprinta normal.
  Probado: retrocede 1.72 m en 1.2 s sin girar (0°).
- **Cómo se implementó:** el proyecto nació 2.5D (Z congelado, un eje). En la sesión del 27-sep
  se migró a 3D: `RigidbodyConstraints.FreezeRotation`, `VerticalMove` en `IInputProvider` y
  `SetVelocity(Vector3, float)`. La rotación empezó con `RotateTowards` a 720°/s, pero la cámara
  giraba demasiado brusco, así que se cambió a `SmoothDampAngle`.
- **Dependencias:** `Camera.main` (si no hay, usa los ejes del mundo), `GroundChecker`.
- **Consideraciones:** la lógica de estados corre en el paso de física (50 Hz). Probado: a 0.1 s
  del arranque va a 1.2 m/s y llega a 5 m/s; al soltar, a 0.12 s va a 3.1 m/s y se detiene.
- **Animaciones:** RunBackward (LowPoly) · Idle (LowPoly) · Walk, Jog Forward, Run (DPS) · Sprint
  (LowPoly), en un blend 1D por `Speed` (`arquitectura.md` §5.10).
- **Falta:** bloquear el movimiento al morir o durante animaciones de daño (GDD).

### F02 — Sprint ✅
- **Objetivo:** +~40 % de velocidad manteniendo Shift; se cancela al recibir daño, bloquear o
  atacar (GDD §5.2).
- **Archivos:** `PlayerInputHandler.cs` (`IsSprintHeld`), `PlayerMovement.cs` (`SprintMultiplier`
  1.4, `SprintSpeed`, `CanSprint`, `CancelSprint`), `PlayerGroundedStates.cs` (`PlayerRunState`).
- **Cómo funciona:** en `Run`, `IsSprint = CanSprint` en cada tick: Shift mantenido y sprint no
  cancelado. Velocidad = `BaseSpeed × 1.4` (5 → 7 m/s). Atacar, bloquear o recibir daño
  (`HealthSystem.OnDamageReceived`) llaman `CancelSprint()` y el sprint no vuelve hasta soltar Shift.
  **Se conserva** en `Jump`, `Fall` y `Vault` (GDD §5.2, §5.4). La animación pasa de Run a Sprint en
  el blend tree `Locomotion`.
- **Cómo se implementó:** el auto-sprint (3 s corriendo) se reemplazó el 2026-09-30 por Shift
  mantenido (P1). Shift era la tecla del slide, que pasó a C.

### F03 — Salto y caída ✅
- **Archivos:** `PlayerAirStates.cs` (`PlayerJumpState`, `PlayerFallState`).
- **Cómo funciona:** el salto solo sale del suelo (`IsGrounded`). `Jump.Enter` aplica `JumpSpeed`
  (7) en Y y conserva la velocidad horizontal. En el aire hay control total con `MoveDirection`. Al
  llegar al apex (`vy ≤ 0`) pasa a `Fall`, o a `Idle` si ya está en el suelo. `Idle` y `Run` pasan a
  `Fall` si llevan más de 0.15 s sin suelo (`AirTime > FallGraceTime`), por ejemplo al salir de una
  orilla. `Fall` detecta cornisas y aterriza en `Run` (con input) o `Idle`. Si se salta o cae
  sprintando, conserva la velocidad de sprint (F02). Animaciones: Jump_Up (LowPoly) y Fall A Loop
  (DPS); al aterrizar tras más de 0.35 s de caída se ve *Falling To Landing* (sin input) o
  *Land To Run Forward* (con input), solo visual (P19).
- **Cómo se implementó:** el 2026-09-30 se agregó `PlayerFallState` (resuelve T13, P13) y, en la
  integración del parkour, el aterrizaje.
- **Consideraciones:** no hay coyote time ni jump buffer. Espacio junto a un obstáculo bajo hace
  vault en lugar de saltar (F04).

### F04 — Vault ✅
- **Objetivo:** pasar obstáculos bajos manteniendo el impulso, con Espacio (GDD §5.4).
- **Archivos:** `PlayerParkourStates.cs` (`PlayerVaultState`), `EnvironmentChecker.cs`
  (`TryFindVault`), `VaultInfo.cs`, `PlayerGroundedStates.cs` (`Idle` y `Run`),
  `PlayerAnimator.cs` / `PlayerAnimatorIK.cs` (IK de la mano).
- **Cómo funciona:** en `Idle` o `Run`, Espacio llama `PlayerVaultState.TryStart`: si
  `TryFindVault` encuentra delante (en la dirección del input o del frente) un obstáculo en layer
  Obstacle de 0.45–1.2 m de alto y hasta 1.5 m de profundidad, con suelo detrás, entra a `Vault`;
  si no, salta. `Vault` gira hacia el obstáculo, pone el cuerpo kinemático y lo lleva en 0.6 s del
  punto de inicio al de aterrizaje (0.6 m detrás de la cara trasera), con un arco extra si el
  obstáculo supera la altura que ya levanta la animación. Al salir conserva el impulso (velocidad
  de correr o de sprint) y pasa a `Run` o `Idle`. La mano izquierda se apoya en la cima con IK,
  ponderado por la curva `LHandCurve` del clip.
- **Animación:** *Vault1* (VaultFence) del Dynamic Parkour System, acelerada a 0.6 s.
- **Cómo se implementó (2026-09-30):** se adaptó `VaultObstacle` del DPS (P15, P17), midiendo el
  obstáculo con rayos en lugar de su escala y usando layers en lugar de tags. Resuelve T14
  (`vaultHeightCheck` ahora es la altura máxima). Probado en el circuito: obstáculos de 0.6 m,
  1.0 m y 1.1 m × 1.2 m de ancho; el de 1.6 m provoca un salto.
- **Consideraciones:** el cuerpo es kinemático durante el vault, así que no choca con nada en esos
  0.6 s; el aterrizaje se comprueba antes de empezar.

### F05 — Ledge grab / climb ⚠️ Fuera del GDD
- **Archivos:** `PlayerParkourStates.cs` (`LedgeGrab`, `LedgeClimb`), `EnvironmentChecker.IsLedgeDetected`.
- **Cómo funciona:** en `Jump` o `Fall`, si centro y cabeza tocan pared y un raycast hacia abajo
  encuentra la esquina, el cuerpo queda kinemático colgado (offset 0.4 atrás, 1 abajo). Espacio sube
  en 0.9 s (60 % vertical, 40 % horizontal) y termina de pie sobre la cornisa. Presionar la
  dirección opuesta (`Dot < −0.5`) suelta y pasa a `Fall`.
- **Animaciones (DPS):** *Idle To Braced Hang* → *Hanging Idle* (colgado) y *Braced Hang To Crouch*
  (subida).
- **Corrección del 2026-09-30:** la subida dejaba el centro del torso a 0.1 m sobre la cornisa, con
  los pies ~0.9 m dentro del muro; ahora termina a media altura del cuerpo sobre la cima. Probado:
  queda de pie a 3.0 m sobre la losa en el muro de 3.0 m. Los muros deben ser más altos que lo que
  sube un salto (~2.5 m), o el jugador cae encima en lugar de colgarse.
- **Nota:** el GDD §28 limita el parkour a salto, sprint y vault, pero el equipo decidió
  **conservarlo activo** (P2).

### F07 — Slide ⚠️ Fuera del GDD
- **Archivos:** `PlayerGroundedStates.cs` (`PlayerSlideState`).
- **Cómo funciona:** C en `Run` **mientras se esprinta** reduce el collider al 50 % y avanza a
  `SlideSpeed` 9 por 0.8 s (antes 14 por 0.7 s). No se levanta si hay techo
  (`HasCeilingOverhead`), así que pasa bajo barras y túneles por física. Animación: *Running
  Slide* del Dynamic Parkour System. Probado: pasa bajo la barra de 1.2 m del circuito.
- **Nota:** se conserva activo (P2). Pasó de Shift a C el 2026-09-30 (P1). Del slide del DPS solo se
  tomó la animación: su detección por tags es menos robusta que la reducción del collider.

### F08 — Esquivar ✅
- **Objetivo:** Q + dirección, 0.5 s, 0.2 s invulnerable, cooldown 1 s. No durante un ataque ni
  una animación de daño (GDD §5.5).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerDodgeState`), `HealthSystem.ActivateIFrames`.
- **Cómo funciona:** Q desde `Idle` o `Run` (solo en el suelo y si `CanDodge`: pasó 1 s desde la
  última esquiva). La dirección es el input de movimiento relativo a la cámara en ese momento (sin
  input, hacia el frente) y no cambia durante la esquiva. Dash a 12 u/s por 0.5 s con
  `ActivateIFrames(0.2)`. El cuerpo no gira mientras esquiva, así que `LocalDirection` elige el roll
  (adelante, atrás, izquierda, derecha) en el blend tree 2D `Dodge`. Luego va a `Run` o `Idle`.
- **Consideraciones:** no se puede esquivar desde un ataque porque los estados de ataque no tienen
  esa transición. Cuando existan animaciones de daño (F29), habrá que bloquearla también ahí.

### F09 — Ataque ligero ✅
- **Objetivo:** golpe (J), daño 10 desarmado, hasta 3 encadenados, ~0.25 s (GDD §5.6).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerLightAttackState`), `Core/Combat/Hitbox.cs`,
  `Core/Combat/WeaponHolder.cs` (opcional).
- **Cómo funciona:** J. Al empezar, el personaje gira hacia el input (si hay) y avanza a 3 m/s
  durante 0.12 s (impulso); luego se planta. `Hitbox.Activate()` pega a los 0.1 s, cuando el puño
  se extiende en la animación, a los `IDamageable` dentro de la esfera (radio 0.6, centrada 0.6 m
  al frente del torso, layer Enemy). Daño desarmado 10 (`WeaponHolder`). Dura 0.25 s y luego queda
  una ventana hasta 0.5 s para encadenar. **Las pulsaciones de J o K durante el golpe se guardan
  (buffer)** y se usan al abrir la ventana.
- **Consideraciones:** los i-frames del enemigo (0.2 s) son menores que la cadencia de golpes
  (≥ 0.25 s), así que cada golpe del combo hace daño. Probado: un segundo J pulsado a los 0.07 s se
  encadena.
- **Animación:** alterna PunchRight (golpes 1 y 3) y PunchLeft (golpe 2), acelerados a 0.5 s, con
  cross-fade de 0.05 s.
- **Cómo se implementó:** impulso, giro, sincronización del golpe y buffer el 2026-09-30 (P18).

### F10 — Ataque fuerte 🟡
- **Objetivo:** patada (K), daño 20 desarmado, 0.8 s, retroceso, vulnerable si falla (GDD §5.7).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerHeavyAttackState`).
- **Cómo funciona:** K. Gira hacia el input, avanza a 4 m/s durante 0.15 s y se planta; activa la
  hitbox a los 0.3 s (pico del golpe en la animación) con daño 20 desarmado (`WeaponHolder`) y dura
  0.8 s sin cancelación; al terminar pasa a `Run` si hay input o a `Idle`. Animación:
  MeleeAttack_OneHanded acelerada a 0.8 s, **placeholder** porque no hay clip de patada (T17).
- **Problemas:** el knockback (`WeaponHolder.GetKnockback`) no se aplica.
- **Falta:** retroceso y animación de patada.

### F11 — Combo 🟡
- **Objetivo:** J → J → K, reinicio si pasan más de 0.5 s (GDD §5.9).
- **Cómo funciona hoy:** `LightAttack` puede volver a entrar hasta 3 veces (`_chainCount`) o pasar
  a `HeavyAttack` con J o K pulsados durante el golpe (buffer) o dentro de la ventana de 0.25 s a
  0.5 s. Permite J → K, J → J → K y J → J → J → K. El GDD define exactamente J → J → K. Probado:
  J, J, K produce dos golpes ligeros y el fuerte, con 1.4 m de avance en total.
- **Falta:** definir si se restringe a J → J → K y que con arma use las animaciones del arma.

### F12 — Bloqueo ✅
- **Objetivo:** mantener L, −70 % de daño **solo frontal**, reduce la movilidad (GDD §5.8).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerBlockState`), `Core/Interfaces/IDamageModifier.cs`,
  `HealthSystem.cs`, `PlayerMovement.cs`.
- **Cómo funciona:** mantener L inmoviliza al personaje y cancela el sprint. Mientras bloquea,
  `HealthSystem.TakeDamage` pasa el daño por `IDamageModifier` (`PlayerMovement` →
  `PlayerBlockState.ModifyIncomingDamage`): si la fuente está a ±60° del frente, recibe el 30 %
  (redondeado); por la espalda o los lados, el daño completo. J mientras bloquea contraataca.
  Animación: transición Ch45 a guardia → BlockingLoop → transición Ch45 de vuelta.
- **Cómo se implementó:** 2026-09-30 (resuelve T4) con el diseño de `arquitectura.md` §7.
- **Consideraciones:** "reduce la movilidad" se interpreta como inmóvil, igual que antes. No se ha
  probado contra enemigos porque ninguno hace daño todavía (T2).

### F13 — Vida, daño e i-frames 🟡
- **Objetivo:** 100 HP, nunca fuera de [0, máx], 0.5 s de invulnerabilidad tras un golpe,
  feedback visual y sonoro (GDD §5.11).
- **Archivos:** `Core/Combat/HealthSystem.cs`, `Core/Interfaces/IDamageable.cs`, `Hitbox.cs`.
- **Cómo funciona:** `TakeDamage(amount, source)` ignora el daño si está muerto o en i-frames,
  aplica clamp y dispara `OnDamageReceived`, `OnHealthChanged` y `OnDeath`. Los i-frames se miden con
  `Time.time`, sin corrutinas. `ActivateIFrames(d)` desplaza la marca de tiempo sin acortar unos
  i-frames que ya estén activos. `Heal` no revive; `InitializeHealth` sí.
- **Consideraciones:** está en el Player.prefab (i-frames 0.5 s, GDD) y en el Enemy.prefab
  (0.2 s, por debajo de la cadencia del combo). **Nadie escucha `OnDeath` del jugador**, así que
  morir no tiene efecto. No hay HUD ni barra de vida (GDD §16, P7).
- **Falta:** regeneración (F14), muerte y reaparición (F19), feedback (F29).

### F14 — Regeneración de vida ⬜
- **Objetivo:** regenerar automáticamente tras unos segundos sin daño (GDD §5.11).
- **Plan:** componente `HealthRegen` (ver `arquitectura.md` §7). El GDD no define el tiempo ni la
  tasa; hay que acordarlos en equipo.

### F15 — Caída mortal ⬜
- **Objetivo:** muerte instantánea al caer desde gran altura o en un barranco (GDD §5.11, §5.12).
- **Plan:** medir la altura de la caída + trigger `KillZone` que llama `HealthSystem.InstantKill()`
  (el método ya existe). El GDD no define la altura; hay que acordarla. Para medir caídas sin
  salto ya existe `PlayerFallState` (F03): la comprobación va en su aterrizaje.

### F16 — Sistema de armas 🟡
- **Objetivo:** recoger con E katana (20/35), yari (18/30, más alcance) o kanabo (30/50, más
  lento). Una a la vez; al recoger otra se suelta la actual (GDD §5.10, §18).
- **Archivos:** `Core/Combat/WeaponData.cs` (SO), `Core/Combat/WeaponHolder.cs`.
- **Cómo funciona hoy:** `WeaponHolder` está en el Player.prefab sin arma inicial, así que los
  ataques usan los valores desarmados del GDD (10/20). `Equip(data)` guarda el arma, ajusta el
  radio del `Hitbox` y dispara `OnWeaponChanged`. Los estados de ataque piden el daño a
  `WeaponHolder`.
- **Problemas:** **no hay assets `WeaponData`** y no existe pickup, drop, modelo en la mano ni
  diferencia de velocidad (kanabo más lento).
- **Falta:** todo lo anterior. Plan en `arquitectura.md` §7.

### F17 — IA de enemigos 🟡
- **Objetivo:** arquero (50 HP, flechas de 10, distancia), guerrero ligero (80, cortes de 12,
  rápido, esquiva) y guerrero pesado (150, golpes de 25, lento, bloquea, embiste). FSM
  `Idle → Detectar → Acercarse → Atacar → Defenderse → Buscar → Regresar`, sin salir de su zona
  (GDD §5.14, §12, §21).
- **Archivos:** `Enemy/Enemy.cs`, `Enemy/StateMachine/*`, `Core/Combat/EnemyData.cs`.
- **Cómo funciona el código:** estados `Patrol → Chase → Attack → Dead`. La detección es una esfera
  de `DetectionRange` filtrada por la layer `Player` + tag `Player` + línea de visión, y la pérdida
  del objetivo es por `LoseTrackRange`. El ataque para al enemigo, activa la hitbox al 30 % de
  `AttackDuration` y respeta `AttackCooldown`. Al morir espera 1.5 s y vuelve al pool; al salir del
  pool revive con `InitializeHealth`.
- **Cómo se implementó:** commit `796fac3` (29-sep), con la misma arquitectura que el jugador. El
  2026-09-30 se corrigió que al morir desactivaba su propio GameObject (el `Hitbox` está en la
  raíz) y nunca volvía al pool, que al reaparecer no revivía, que se detectaba a sí mismo como
  jugador (tag `Player` + overlap sin máscara) y el `GetComponent` por tick. Se eliminaron
  `Looter`/`Brute`.
- **Problemas:** movimiento **2.5D** (Z congelado, eje X), el `Enemy.prefab` no tiene el script,
  faltan los estados Defenderse, Buscar y Regresar, no hay zona asignada ni ataque a distancia y
  no hay tipos del GDD.
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
- **Cómo funciona:** lee `Keyboard.current` en `Update` y bufferiza los triggers, con los
  bindings del GDD §14 (P1): WASD/flechas, Shift (sprint mantenido), Espacio, J, K, L (mantener),
  Q y C (slide). Hay `InputActionReference` opcionales (incluido Sprint), todos vacíos. "Last input
  wins" si se presionan direcciones opuestas. No hay asset `.inputactions`.
- **Falta:** un asset `.inputactions` con gamepad (P6) y las acciones Interact (E), Look y Pause.

### F23 — Animación 🟡
- **Objetivo:** que el personaje se vea correctamente y anime cada acción (GDD pilar 2; el riesgo
  §28 pide reutilizar animaciones).
- **Archivos:** `Player/PlayerAnimator.cs`, `Player/PlayerAnimatorIds.cs`,
  `Characters/Player/PlayerAnimator.controller`, `Characters/Player/Textures/`,
  `Characters/Player/Animations/`, `Editor/PlayerAnimationSetup.cs`.
- **Cómo funciona:** ver `arquitectura.md` §5.10. La FSM avisa cada cambio de estado y
  `PlayerAnimator` hace cross-fade (0.15 s; 0.05 s en ataques, aterrizajes y parkour) al estado
  equivalente del Animator; `Speed` (con signo) mueve el blend WalkBackward → Idle → Walk → Jog →
  Run → Sprint. Sin root motion. 17 estados, 4 parámetros, IK Pass para la mano del vault.
- **Cómo se implementó (2026-09-30):**
  - El personaje se veía sin textura porque Unity no usa las texturas embebidas en un FBX hasta
    extraerlas. Se extrajeron y el material del FBX ahora tiene Base Map y Normal Map.
  - La luz direccional de `Level-1` estaba a 0.3; se subió a 1.
  - Clips Humanoid `LowPoly` retargeteados a Ch45 y dos transiciones Ch45 (P11).
  - La validación en batch muestrea cada clip sobre Ch45 (`AnimationMode`) y comprueba que mueve el
    rig sin poses rotas.
  - Integración del parkour (2026-09-30): 11 clips del Dynamic Parkour System (MIT) para
    locomoción, caída, aterrizaje, vault, slide y cornisa; *RunBackward* para caminar hacia atrás.
- **Falta:** clip propio de patada y una caminata hacia atrás real (T17); hit y muerte (con
  F19/F29); calibrar Run/Sprint contra la zancada (T16); confirmar la licencia de `LowPoly` (T15).

### F31 — Auto step ✅
- **Objetivo:** que los escalones y bordillos bajos no detengan la carrera (movimiento base del
  Dynamic Parkour System; no es una mecánica del GDD).
- **Archivos:** `PlayerMovement.cs` (`TryAutoStep`, `StepHeight`, `stepLayer`),
  `PlayerGroundedStates.cs` (`Run.PhysicsUpdate`).
- **Cómo funciona:** en `Run`, si un rayo a 5 cm del suelo choca en la dirección de movimiento, otro
  a `StepHeight` (0.4 m) no choca y la cima está a menos de 0.4 m, sube el cuerpo hasta la cima.
  Ignora pendientes (normal con `|y| > 0.3`) y lo que sea más alto (muros, obstáculos de vault).
- **Cómo se implementó:** adaptado de `AutoStep` del DPS el 2026-09-30 (P17). Probado: sube cinco
  escalones de 0.25 m hasta la plataforma de 1.25 m.

### F32 — Circuito de parkour y pruebas automáticas ✅
- **Objetivo:** probar cada movimiento de forma aislada y combinada, a mano y automáticamente.
- **Archivos:** `Editor/ParkourTestCircuitBuilder.cs`, `Editor/ParkourPlayModeTest.cs`,
  `Assets/Tests/ParkourCircuit/Materials/`, objeto `ParkourTestCircuit` de `Level-1`.
- **Circuito** (losa propia con top en y = 0.55; el `Spawner` está en su entrada, P20). Cinco
  carriles hacia −Z con cartel a la entrada:

  | Carril | x | Obstáculos | Prueba |
  |---|---|---|---|
  | VAULT | −8 | 0.6 m · 1.0 m · 1.1 m de 1.2 m de ancho · 1.6 m (demasiado alto) | Espacio cerca del obstáculo |
  | SLIDE | −3 | Barra a 1.2 m (1 m) · túnel a 1.2 m (4 m) | Shift + C |
  | AUTO STEP | 2 | 5 escalones de 0.25 m → plataforma de 1.25 m · bordillo de 0.35 m | Caminar |
  | LEDGE | 7 | Muros de 3.0 m y 3.5 m, 2 m de fondo | Saltar contra el muro, Espacio para subir |
  | COMBINADO | 12 | Bordillo → vault 1.0 m → barra de slide → muro de 3.0 m | Todo seguido |

  Pasillo libre en x = 17 para movimiento general. Regenerar: **Tools → Warrior Woke → Construir
  Circuito de Parkour** (también valida que nada de la escena lo invada).
- **Prueba automática** (**Probar Personaje en Play Mode**): entra a Play Mode, agrega un teclado
  virtual del Input System (pasa por `PlayerInputHandler` igual que el teclado real) y comprueba:
  spawn y acceso al circuito; Idle en el suelo; aceleración, carrera a 5 m/s y frenado; caminar
  hacia atrás sin girar; sprint a 7 m/s; salto, caída y aterrizaje; los tres vaults y el salto ante
  1.6 m; slide bajo la barra; auto step; agarrarse y subir la cornisa; combo J → J → K con buffer e
  impulso; esquiva hacia atrás; bloqueo (6 de daño de frente, 20 por la espalda); cero errores en
  consola. Resultado del 2026-09-30: **36/36**.
- **Consideraciones:** no prueba la cámara con ratón (no existe), enemigos ni cómo se ven las
  animaciones; eso se revisa jugando.

### F24 — Spawning y object pooling ✅
- **Archivos:** `Core/Spawning/{ObjectPoolManager,Spawner,ReturnToPoolDelay}.cs`,
  `Core/Interfaces/IPoolable.cs`, prefabs `GameManager` y `Spawner`.
- **Cómo funciona:** ver `arquitectura.md` §5.6. En `Level-1`, el Player nace del pool `player`
  gracias al `Spawner` (OnStart).
- **Cómo se implementó:** commit `4d9222d` (18-sep). El 27-sep se bajó el `Spawner` de Y = 40 a
  Y = 2 y se agregó el `Ground`, porque el jugador aparecía en el aire sin suelo. El 30-sep,
  `Spawn` pasó a colocar el objeto antes de activarlo (la cámara hacía snap a la posición vieja).
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
  uses `~0` en queries calientes (`Enemy.DetectPlayer` filtra por la layer `Player`).
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
  cooldowns como **campo de instancia**, no `static` (un `static` se compartiría entre instancias),
  como ya hace `PlayerDodgeState`.
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
  garbage; úsalo solo en eventos puntuales (equipar arma, morir), nunca por frame. Lo mismo
  aplica a actualizar textos de UI: solo cuando el valor cambia de forma discreta.
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
- Comprueba tag y layer de cada prefab (un enemigo con tag `Player` se detectaba a sí mismo).
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
   ¿Está en el GDD? Si no, no se agrega (P7, P8).
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
| 2026-09-29 | `c576be0` | Axel | Decisiones P2 y P3: wall jump desactivado (transición comentada, código conservado) y sistema de XP eliminado (`PlayerXpSystem`, `IXpReceiver`, `EnemyData.xpReward`, la llamada en `Enemy.OnDeath` y comentarios de recompensa en `Looter`/`Brute`). Compilación verificada en batch mode sin errores. |
| 2026-09-30 | `7f08719` | Angel | `PlayerStamina` (gasto por sprint) y `PlayerHUD` (barras de vida y estamina creadas en runtime). No se documentó y contradecía el GDD §5.2 y §16; se retiró en la entrada siguiente. |
| 2026-09-30 | (ver `git log`) | Axel | Auditoría completa y limpieza contra el GDD (detalle abajo). Scripts compilados sin errores con el compilador de Unity (runtime y Editor). |
| 2026-09-30 | (ver `git log`) | Axel | Personaje jugable: texturas, Animator, controles del GDD, sprint, caída, bloqueo y esquiva (detalle abajo). Validado en batch mode; falta Play Mode. |
| 2026-09-30 | (ver `git log`) | Axel | Parkour con el Dynamic Parkour System, locomoción con aceleración, caminar hacia atrás, combate más dinámico, circuito y prueba en Play Mode (detalle abajo). 36/36 en Play Mode. |

### Detalle — parkour y animaciones (2026-09-30)

Formato del GDD §29: **problema → causa → solución → estado**.

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| El vault era automático y medía mal el obstáculo | Distancia fija de 2 m; `vaultHeightCheck` sin uso (T14) | Vault con Espacio y `TryFindVault` adaptado del DPS, con animación e IK de mano | ✅ |
| Animaciones de parkour provisionales | No había clips de vault, slide ni cornisa | 11 clips del Dynamic Parkour System (MIT) | ✅ |
| Cambio brusco Idle ↔ Run | La velocidad se asignaba de golpe | Aceleración/desaceleración + blend Walk y Jog | ✅ |
| No había forma de retroceder sin girar | El personaje siempre mira hacia donde se mueve | `IsBackpedaling` + Speed con signo + RunBackward | ✅ |
| La caminata Ch45 hacia atrás se veía agachada | El clip es una caminata agachada (cabeza a ~1.0 m de los pies) | Se usa *RunBackward* de LowPoly a ×0.6 | ✅ (T17) |
| Ataques estáticos y J rápidos perdidos | El golpe se plantaba y aplicaba daño al instante; sin buffer | Impulso, giro hacia el input, daño sincronizado, buffer de combo | ✅ |
| Subir la cornisa dejaba los pies dentro del muro | El destino era la esquina + 0.1 m | Destino a media altura del cuerpo sobre la cima | ✅ |
| Los escalones detenían la carrera | No había auto step | `TryAutoStep` (DPS) | ✅ |
| Los obstáculos del circuito quedaban hundidos | `Ground` es una cápsula (cúpula de 0.5 m) | Losa propia para el circuito; Ground documentado (T19) | ✅ |
| Desde el spawn no se podía salir | `House_01_cyber` encierra el spawn original | `Spawner` en la entrada del circuito (P20, T20) | ✅ |
| La primera prueba automática no pulsaba teclas | El ejecutor de la prueba no corría corrutinas anidadas | Pila de corrutinas en `ParkourPlayModeTest` | ✅ |

### Detalle — personaje jugable (2026-09-30)

Formato del GDD §29: **problema → causa → solución → estado**.

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| El personaje se veía azul / sin textura | Las 5 texturas de Ch45 venían embebidas en `character.fbx` y Unity no las usa hasta extraerlas: el material URP quedaba sin Base Map ni Normal Map. Además, la luz direccional estaba a 0.3 y casi toda la luz venía del cielo azul | Texturas extraídas a `Characters/Player/Textures/` (Normal como *Normal Map*); luz a 1 | ✅ |
| Personaje sin animaciones | No había Animator Controller | `PlayerAnimator.controller` + `PlayerAnimator` dirigido por la FSM (P11, D10) | ✅ (clips provisionales, T17) |
| Los FBX de animación Ch45 no se veían en Unity | Estaban en la raíz del repo, fuera de `Assets/` | Solo las 2 transiciones de guardia entraron a `Characters/Player/Animations/` (P11) | ✅ |
| Controles distintos al GDD | Venían del diseño anterior | J/K/L/Q, Shift sprint, Espacio, slide en C (P1) | ✅ (faltan E y ESC) |
| Sprint automático | Diseño anterior | Shift mantenido, +40 %, cancelado por daño/bloqueo/ataque (F02) | ✅ |
| Velocidad de 8 m/s, demasiado alta para la animación | Valor heredado | `BaseSpeed` 5 (correr), sprint 7 (P12) | 🟡 por calibrar (T16) |
| No había estado de caída (T13) | — | `PlayerFallState` | ✅ |
| El bloqueo no reducía daño (T4) | Nadie leía el multiplicador | `IDamageModifier` en `HealthSystem`, −70 % solo frontal | ✅ |
| La esquiva ignoraba la dirección | Usaba `transform.forward` | Dirección del input + roll direccional | ✅ |
| La primera validación de poses daba siempre la misma altura | `Animator.Update` no evalúa el retarget en modo Editor | Se muestrea con `AnimationMode.SampleAnimationClip` | ✅ |

### Detalle — limpieza del 2026-09-30

Formato del GDD §29: **problema → causa → solución → estado**.

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| Estamina y HUD fuera del GDD | Se agregaron sin revisar el GDD §5.2 (sprint sin recurso) ni §16 (sin barras de vida) | Se eliminaron `PlayerStamina`, `PlayerHUD` y sus conexiones (P7) | ✅ |
| Código fuera del GDD sin uso | Restos del diseño 2.5D anterior | Se eliminaron wall jump, `Looter`/`Brute`, `initial_floor.prefab` e `InputSystem_Actions` (y su registro como project-wide actions) (P8) | ✅ |
| `LightingData.asset` aparecía modificado sin hornear | Es binario, pero `*.asset text eol=lf` hacía que git le convirtiera los finales de línea (riesgo de corromperlo) | Se borró la iluminación horneada de `Level-1`, se quitó su referencia en la escena, se ignoró `Assets/Scenes/*/` y se marcó `LightingData.asset` como `binary` (P9) | ✅ |
| El build se llamaba `pin-ball` | `ProjectSettings` venía de una plantilla | `productName` Awakened Warrior, `companyName` SUNUX GAMES, identificador `com.SUNUXGAMES.AwakenedWarrior` (P10) | ✅ |
| El 2º golpe del combo no hacía daño | El enemigo tenía los mismos 0.5 s de i-frames que el jugador y los golpes van cada ≥ 0.25 s | i-frames del `Enemy.prefab` a 0.2 s | ✅ |
| El golpe del jugador casi no tenía alcance y pegaba por la espalda | La esfera del `Hitbox` estaba en el centro del torso | `Hitbox.localOffset` (0.6 m al frente en el Player) | ✅ |
| Daño desarmado 10/10 en vez de 10/20 | `WeaponHolder` no estaba en el Player.prefab y su golpe desarmado valía 8 | `WeaponHolder` agregado al prefab; desarmado 10/20 (T6) | ✅ |
| El enemigo desaparecía al morir y no volvía al pool | `EnemyDeadState` desactivaba `Hitbox.gameObject`, que es el propio enemigo; `OnSpawn` usaba `Heal`, que no revive | Se quitó el `SetActive`; `OnSpawn` usa `InitializeHealth` | ✅ |
| El enemigo se detectaba a sí mismo | Tag `Player` en `Enemy.prefab` + overlap sin máscara | Tag `Untagged` y `playerLayer` en `DetectPlayer` (T1) | ✅ |
| El salto perdía el sprint | `Run.Exit` apagaba `IsSprint` antes de entrar a `Jump` | Reinicio movido a `Idle.Enter` y a las salidas explícitas | ✅ |
| Se podía esquivar sin límite | Nadie consultaba `CanDodge`, y era `static` | Transiciones consultan `CanDodge`; cooldown de instancia (T5) | ✅ |
| Una esquiva podía acortar los i-frames de un golpe | `ActivateIFrames` sobrescribía la marca de tiempo | Usa `Mathf.Max` | ✅ |
| La cámara hacía snap a la posición vieja del Player | El pool activaba el objeto antes de moverlo | `Spawn` coloca y luego activa | ✅ |
| Constante de bloqueo en 95 % | Venía del GDD anterior | 0.3 (−70 %, GDD §5.8). Sigue sin aplicarse (T4) | 🟡 |
| Deuda menor | — | `GetComponent` por tick en `Enemy` (T8), comentarios del GDD anterior (T9), `SetVelocity(float, float)` → `StopHorizontal` (T10), gizmos hacia `forward` (T11), plantilla de input (T12), nombre `"CenterPoint "` con espacio | ✅ |

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
   *(Nota del 2026-09-30: en realidad `Ground` usa la malla Capsule, una cúpula de 0.5 m; ver
   `arquitectura.md` T19.)*
   `initial_floor` (blockout viejo) nunca se colocó en esta escena; se eliminó el 30-sep.
