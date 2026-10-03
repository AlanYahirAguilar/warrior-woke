# Awakened Warrior — Features, historial y buenas prácticas

> Parte de la documentación del proyecto: [`contexto.md`](contexto.md) (qué es el juego) ·
> [`arquitectura.md`](arquitectura.md) (cómo está construido) · **`features.md`** (qué hay
> implementado, cómo se hizo y qué prácticas seguimos). Los estados (✅ 🟡 🔧 📋 ⬜ ⚠️ ⏸️) se
> definen en `contexto.md` §1.
>
> Estado verificado el 2026-10-02, después de la pasada del Parkour Obstacle Standard (ver §5):
> los scripts compilan sin errores, `PlayerAnimationSetup` valida Avatars, material, clips, la curva
> del vault, el contacto de las suelas, Missing Scripts, el Parkour Test Area, los 10 prefabs de
> obstáculos y los 25 obstáculos de la escena contra el estándar, y **`ParkourPlayModeTest` lo
> prueba en Play Mode real** con teclado simulado: **341/341 comprobaciones**, incluido el contacto
> medido sobre el esqueleto, la consistencia de cada tipo de obstáculo desde varias posiciones, la
> calidad del movimiento (momentum, slide, transiciones), las marchas, la orientación de la pose, el
> mantle, el drop y el salto desde la cornisa (F32). Reconstrucción del movimiento (P28): §5. Lo que la prueba no mide (la calidad visual de las poses y la sensación al jugar) falta
> revisarlo jugando.

---

## 1. Resumen de estado

| # | Feature | GDD | Estado |
|---|---|---|---|
| F01 | Movimiento en tercera persona | §5.1 | ✅ |
| F02 | Sprint | §5.2 | ✅ |
| F03 | Salto, caída y aterrizaje | §5.3 | ✅ |
| F04 | Vault | §5.4 | ✅ |
| F05 | Ledge grab / climb / drop / salto de cornisa | — | ⚠️ Fuera del GDD (se conserva, P2; ampliado en P28) |
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
| F21 | Cámara al hombro | §15 | 🟡 orbital con ratón; sin shake ni encuadre de combate |
| F22 | Input | §14 | 🟡 lectura directa, sin gamepad |
| F23 | Animación | Pilar 2 | 🟡 clips provisionales |
| F24 | Spawning y object pooling | (técnico) | ✅ |
| F26 | Menú, pausa, flujo de escenas | §16–§17 | ⬜ |
| F27 | Niveles y mundos | §9–§10 | ⬜ `Level-1` es el Parkour Test Area |
| F28 | Audio | §23 | ⬜ |
| F29 | Feedback de daño (sin HUD) | §16 | ⬜ |
| F30 | Herramientas de Editor | (técnico) | ✅ |
| F31 | Auto step y step down | (técnico) | ✅ |
| F32 | Parkour Test Area y pruebas automáticas | (técnico) | ✅ |
| F33 | Parkour Obstacle Standard y prefabs de obstáculos | (técnico) | ✅ |
| F34 | Marchas: caminar, strafe, retroceso rápido, agacharse | — | ⚠️ Fuera del GDD (aprobado en P28) |
| F35 | Mantle (subirse a bloques de 0.8–1.5 m) | — | ⚠️ Fuera del GDD (aprobado en P28) |

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
| Caminar hacia atrás | No lo define | S sin sprint corre hacia atrás mirando al frente a 3.5 m/s; con Ctrl camina hacia atrás a 1.7 m/s (P16, P28) |
| Salto | "Impulso vertical" sin valor | ~1 m de altura (escala humana) e inercia en el aire (P23) |
| Aterrizaje | No lo define | Según la altura de la caída: absorbe velocidad y la recupera sin bloquear el control (P23) |
| Parkour | Solo salto, sprint y vault (§28) | Además: ledge grab/climb (desde el suelo o en el aire) y slide (se conservan por decisión P2; slide con C corriendo con momentum, P27) y auto step / step down (movimiento base). El wall jump se eliminó |
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
  `MoveDirection × BaseSpeed (5)` con `AccelerateHorizontal` (10 m/s² al acelerar, 13 m/s² al
  frenar), conservando Y; `Idle` frena hasta 0 con la misma desaceleración. Así el blend
  `Locomotion` pasa por Walk y Jog al arrancar y al detenerse. El personaje gira con
  `Mathf.SmoothDampAngle` aplicado por `Rigidbody.MoveRotation`: 0.12 s corriendo y hasta 0.2 s
  esprintando (un cuerpo rápido gira más abierto). El Rigidbody está **interpolado**, así que el
  modelo y la cámara se mueven suaves entre pasos de física. Los clips de locomoción se reproducen
  en el sitio (antes Walk y Jog patinaban hasta 2.2 m por ciclo) y, de pie, las suelas tocan el
  suelo (antes flotaban 11 cm). El IK de pies (`PlayerContactIK`) apoya cada pie en el terreno.
- **Momentum y peso (2026-10-02, P27):** la velocidad sigue la orientación del cuerpo
  (`AccelerateAlongFacing`): un giro curva la carrera y pierde velocidad según lo cerrado que es, y un
  input muy por detrás frena antes de girar. El giro máximo baja de 720°/s parado a lo que permite ~0.9 g de aceleración lateral
  a la velocidad que lleva (P28). El torso se inclina con la aceleración real: hacia la curva en un giro (hasta 10°),
  adelante al acelerar y atrás al frenar. Probado: en un giro de 90° a la carrera la velocidad nunca
  se separa del cuerpo (0°, antes patinaba de lado) y el torso se inclina 10°; al frenar desde el
  sprint pasa de 7.0 a 5.7 m/s en 0.1 s y el torso se echa 6° atrás.
- **Hacia atrás (P16, P28):** con input hacia atrás (S o diagonales) y sin sprint, el cuerpo mira
  al frente de la cámara y **corre hacia atrás a 3.5 m/s** (el ritmo medido de *RunBackward*); con
  Ctrl camina hacia atrás a 1.7 m/s (el walk invertido). Las diagonales hacia atrás usan sus propios
  clips. Con Shift el cuerpo frena y pivota 180° progresivamente. La cámara no gira. Marchas y
  strafe: F34.
- **Cómo se implementó:** el proyecto nació 2.5D (Z congelado, un eje). En la sesión del 27-sep
  se migró a 3D: `RigidbodyConstraints.FreezeRotation`, `VerticalMove` en `IInputProvider` y
  `SetVelocity(Vector3, float)`. La rotación empezó con `RotateTowards` a 720°/s, pero la cámara
  giraba demasiado brusco, así que se cambió a `SmoothDampAngle`.
- **Dependencias:** `Camera.main` (si no hay, usa los ejes del mundo), `GroundChecker`.
- **Consideraciones:** la lógica de estados corre en el paso de física (50 Hz). Probado: a 0.1 s
  del arranque va a 1.0 m/s y llega a 5 m/s; al soltar, a 0.12 s va a 3.4 m/s y se detiene. Un giro
  de 90° a la carrera es progresivo y no muestra la caminata hacia atrás.
- **Animaciones (P28):** blend 2D direccional sobre la velocidad bajo el cuerpo, con cada clip en su
  velocidad medida: Idle, Walk, Jog, Run (el sprint lo reproduce más rápido), WalkBackward (generado),
  RunBackward y sus diagonales, diagonales hacia delante y strafes (`arquitectura.md` §5.10).
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

### F03 — Salto, caída y aterrizaje ✅
- **Archivos:** `PlayerAirStates.cs` (`PlayerJumpState`, `PlayerFallState`),
  `PlayerMovement.cs` (`AccelerateAir`, `RegisterLanding`, `RecoverySpeedScale`), `PlayerAnimator.cs`.
- **Cómo funciona:** el salto solo sale del suelo (`IsGrounded`). `Jump.Enter` aplica `JumpSpeed`
  (4.5 m/s, ~1 m de altura: escala humana, P23) en Y y conserva la velocidad horizontal. En el aire
  el input **corrige el impulso** (`AirAcceleration` 4 m/s²) en lugar de reemplazarlo; sin input se
  conserva casi intacto. Al llegar al apex pasa a `Fall`, o a `Idle` si ya está en el suelo. `Idle`
  y `Run` pasan a `Fall` si llevan más de 0.15 s sin suelo. `Jump` y `Fall` detectan cornisas al
  alcance de las manos (F05). Si se salta o cae sprintando, conserva la velocidad de sprint (F02).
- **Aterrizaje (Fall → Landing → Recovery → Locomotion):** al tocar el suelo, `RegisterLanding`
  mide la caída desde el punto más alto y calcula una severidad: 0 bajo 0.6 m, ~0.25 a 1 m, ~0.6 a
  2 m y ~0.9 a 3 m. Absorbe parte de la velocidad horizontal al instante (hasta quedar en el 30 %) y
  la devuelve durante la recuperación (hasta 0.7 s). El control nunca se bloquea. Animación: `LandRun`
  (aterrizaje ligero con input), `Land` (ligero sin input o medio) o `LandHard` (fuerte, la absorción
  completa a ritmo natural).
- **Animaciones:** Jump_Up (LowPoly; su subida ya no queda en la pose: antes el cuerpo visible
  flotaba 0.58 m sobre el collider en el aire), Fall A Loop, Falling To Landing y Fall A Land To Run
  Forward (DPS). El IK de pies se aplica en el mismo frame del aterrizaje, así los pies no se hunden.
- **Probado:** el salto sube 0.99 m; en el aire las suelas quedan a ≤ 12 cm del collider; salta el
  hueco de 2 m entre plataformas con el impulso de la carrera; caídas de 1 m (ligera), 2 m (media) y
  3 m (fuerte: la velocidad baja al 37 % y a los 1.5 s vuelve a 5 m/s), sin que los pies atraviesen el suelo.
- **Cómo se implementó:** el 2026-09-30 se agregó `PlayerFallState` (resuelve T13, P13) y el
  aterrizaje visual (P19). El 2026-10-01 se pasó a la escala humana y al aterrizaje con peso (P23).
- **Consideraciones:** no hay coyote time ni jump buffer. Espacio junto a un obstáculo bajo hace
  vault, y ante una cornisa al alcance se agarra (F04, F05). La caída mortal (F15) sigue pendiente:
  una caída de 3 m es solo un aterrizaje fuerte.

### F04 — Vault ✅
- **Objetivo:** pasar obstáculos bajos manteniendo el impulso, con Espacio (GDD §5.4).
- **Archivos:** `PlayerParkourStates.cs` (`PlayerVaultState`), `EnvironmentChecker.cs`
  (`TryFindVault`), `VaultInfo.cs`, `ParkourTimings.cs`, `PlayerAnimator.cs` (root motion y
  `MatchTarget`), `PlayerContactIK.cs` (mano y pies), `PlayerGroundedStates.cs` (`Idle` y `Run`).
- **Cómo funciona:** en `Idle` o `Run`, Espacio llama `PlayerVaultState.TryStart`: si
  `TryFindVault` encuentra delante un obstáculo en layer Obstacle de 0.45–1.2 m de alto y hasta
  1.5 m de fondo, con suelo libre detrás, entra a `Vault` (si no, prueba la cornisa y si no, salta).
  El cuerpo pasa a kinemático y **lo lleva el root motion del clip** (P22), warpeado con
  `MatchTarget` en tres fases: despegue más alto si el obstáculo supera los 0.8 m que el clip libra
  solo, la mano izquierda sobre el punto medido de la cima (al 30 % del clip) y los pies sobre el
  punto de aterrizaje (al 78 %). El cuerpo gira hasta quedar perpendicular a la cara del obstáculo.
  El clip se reproduce a la velocidad de la aproximación (×0.8–1.5) y empieza más tarde si el
  obstáculo está más cerca de lo que espera. La mano se queda apoyada con IK mientras la curva
  `LHandCurve` lo indica, y un pie que pasa sobre el obstáculo nunca baja de su cima. Al 82 % del
  clip sale a `Run` o `Idle` con la velocidad de la aproximación (o la de sprint).
- **Animación:** *Vault1* (VaultFence) del Dynamic Parkour System, con root motion.
- **Contexto (P28):** lento frente a un bloque con sitio arriba, Espacio hace mantle en lugar de
  vault (F35); corriendo, vault. Un obstáculo más alto que el vault pero dentro del rango del mantle
  se sube con mantle a cualquier velocidad.
- **Probado** (secciones 02–04, obstáculos estándar): vault bajo (0.6 m), medio (1.0 m), alto
  (1.2 m) y medio de 1.4 m de fondo; corriendo, esprintando, 1.2 m a un lado, en ángulo de 20° y
  desde parado. La mano queda a ≤ 2.1 cm de su punto en todos los casos, los pies pasan 12–58 cm
  sobre la cima, el cuerpo se alinea perpendicular a la cara (0°), aterriza con los pies en el suelo
  y sale a 5 m/s (7 m/s esprintando). **Consistencia:** el mismo tipo da el mismo resultado desde
  todas las posiciones corriendo (aterrizaje con ≤ 10 cm de diferencia). Un obstáculo de 1.6 m
  provoca un salto.
- **Aproximación y velocidad (2026-10-02, P27):** corriendo, Espacio mira más adelante y el vault
  arranca en el punto de despegue del clip (1.2 m de la cara), aunque se pulse antes; caminando o
  parado arranca donde está el cuerpo. El clip se reproduce a la velocidad de su propia carrera
  (5.45 m/s), así que entra y sale a la velocidad de la aproximación (5.00 → 4.99 m/s, 7.0 → 7.2 m/s;
  antes aceleraba un 24 %), y al terminar el cuerpo recibe la velocidad real del root motion. Sin
  teleport: la mayor velocidad entre muestras es 12.9 m/s esprintando (antes un pop de hasta 22 m/s
  en el despegue). Fuera de alcance (2 m, parado), Espacio salta.
- **Vault de obstáculos altos desde parado (2026-10-02):** un obstáculo de más de 0.8 m necesita la
  fase de despegue. Corriendo, el impulso sube el cuerpo a tiempo; desde parado o caminando
  (< 3.5 m/s, la velocidad mínima del clip) y a menos de ~0.91 m de la cara, la pierna delantera
  ya estaba en el obstáculo al empezar el clip y lo atravesaba 14 cm. Ahora en ese caso Espacio
  salta en lugar de hacer el vault (desde 0.91–1.1 m sí hace el vault y libra la cima 13 cm).
- **Cómo se implementó:** el 2026-09-30 se adaptó `VaultObstacle` del DPS (P15, P17), con el
  cuerpo interpolado por código y el clip acelerado a 0.6 s. El 2026-10-01 se pasó a root motion
  con `MatchTarget` (P22). Ver el detalle en §5.
- **Consideraciones:** el cuerpo es kinemático durante el vault, así que no choca con nada; la cara,
  la cima, el fondo y el aterrizaje se comprueban antes de empezar.

### F05 — Ledge grab / climb ⚠️ Fuera del GDD
- **Archivos:** `PlayerParkourStates.cs` (`PlayerLedgeGrabState`, `PlayerLedgeClimbState`),
  `EnvironmentChecker.TryFindLedge`, `LedgeInfo.cs`, `PlayerAnimator.cs`, `PlayerContactIK.cs`.
- **Cómo funciona:** hay dos formas de agarrarse. **Desde el suelo:** Espacio frente a un muro
  cuya cima está a 1.9–2.7 m de los pies; el clip incluye el salto. **En el aire:** un salto o una
  caída que encuentra una cornisa al alcance de las manos (cima a 1.5–2.6 m de los pies en ese
  momento). En los dos casos `TryFindLedge` mide la cara, la normal, el borde exacto y que haya
  espacio para estar de pie arriba. El root motion del clip lleva el cuerpo y `MatchTarget` pone la
  mano izquierda sobre el borde medido; el cuerpo gira hasta quedar de frente al muro, también si el
  muro está en ángulo. Colgado, el IK mantiene **ambas manos en el borde** y **los pies apoyados en
  el muro**, y el cuerpo se ajusta para que las manos animadas lleguen solas. **Espacio sube** (se
  puede pulsar durante el agarre y la subida sigue sin pausa): las manos se quedan en el borde
  mientras el cuerpo tira hacia arriba, luego se apoyan en la cima, y `MatchTarget` deja los pies
  sobre el punto de pie medido. Presionar la dirección contraria al muro suelta y pasa a `Fall`; no
  se puede volver a agarrar en 0.4 s.
- **Animaciones (DPS):** *Idle To Braced Hang* (root motion) → *Hanging Idle* (colgado) y *Braced
  Hang To Crouch* (subida, root motion).
- **Probado** (secciones 06 y 07, obstáculos estándar): cornisa de 2.2 m desde parado, corriendo
  (y soltándose), con Espacio doble, 1.2 m a un lado y girada 30°; muro de escalada de 3.0 m saltando
  desde parado y corriendo. Las manos quedan a ≤ 3.6 cm del borde, nada atraviesa el muro, el cuerpo
  mira al muro (0°), sube y queda de pie arriba con las suelas sobre la cima. En la escalada
  encadenada sube la cornisa de 2.2 m y, desde su terraza, otra hasta 4.4 m; luego cae a la terraza
  y baja por la escalera.
- **Cómo se implementó:** el 2026-09-30 colgaba con un offset fijo bajo una "esquina" medida 0.6 m
  por delante de la cabeza, y subía con una trayectoria lineal. El 2026-10-01 se reescribió con
  detección real del borde, root motion, `MatchTarget` e IK (P22). Ver el detalle en §5.
- **Nota:** el GDD §28 limita el parkour a salto, sprint y vault, pero el equipo decidió
  **conservarlo activo** (P2). Solo hay braced hang: sin muro bajo el borde, los pies cuelgan (T17).

### F07 — Slide ⚠️ Fuera del GDD
- **Archivos:** `PlayerGroundedStates.cs` (`PlayerSlideState`), `PlayerMovement.ShrinkCollider`.
- **Cómo funciona (contextual desde el 2026-10-02, P27):** C en `Run` si hay momentum (≥ 3.9 m/s),
  suelo plano y espacio libre a la altura del slide. El collider baja a la mitad **conservando su
  base en el suelo**. El cuerpo conserva la velocidad que traía (máximo 7.5 m/s) y la pierde con
  fricción (4 m/s², 9 si se suelta el input); frena para no chocar con un obstáculo delante; bajo
  techo sigue a 2.5 m/s hasta tener sitio. Termina por momentum, espacio, input soltado o contrario,
  o la ventana de 2 s; Espacio encadena un vault (si hay un obstáculo saltable delante, espera a
  tenerlo al alcance) o un salto. Animación (P28, Quaternius CC0): *Slide_Start* (más rápida cuanto
  más rápida la entrada) → *Slide_Loop*, un **bucle real** que dura lo que dura el slide (se encadena en
  seco: su primer frame es el último de la entrada) → *Slide_Exit*. Una mano se apoya en el suelo y el
  IK la mantiene encima; el torso se sostiene en ella si el hombro no llega. Detalle:
  `arquitectura.md` §5.15.
- **Por qué se rompía:** el sub-clip *Slide* (un tramo de un slide continuo) se importaba como bucle
  y la pose saltaba atrás cada 0.37 s, y la FSM salía siempre a los 0.8 s, sin importar velocidad,
  espacio ni input.
- **Probado:** pasa bajo la barra y el túnel estándar sin que la cabeza los atraviese; esprintando
  recorre 4.95 m en 1.09 s y corriendo 2.04 m en 0.58 s; soltando el input frena en 0.51 s y se
  detiene; un solo slide y cero reinicios de la pose; la carrera sigue sin saltos de velocidad; hacia
  un obstáculo bajo se detiene a 1 m de él ("obstáculo") o, con Espacio, encadena el vault; con
  Espacio en abierto salta conservando la velocidad (±0.1 m/s); caminando, C no hace nada.
- **Nota:** se conserva activo (P2). Pasó de Shift a C el 2026-09-30 (P1); desde P27 no exige sprint,
  sino momentum. Del slide del DPS solo se
  tomaron las animaciones: su detección por tags es menos robusta que la reducción del collider.

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
- **Cómo funciona (desde el 2026-10-02, P5/P28):** cámara orbital al hombro con yaw y pitch propios
  que mueve el ratón; no sigue la orientación del cuerpo, así que girar, retroceder o dar media
  vuelta no la arrastran. `SphereCast` para no atravesar paredes (ignora al Player). Cursor bloqueado;
  Escape lo libera y un clic lo bloquea. Detalle: `arquitectura.md` §5.7.
- **Probado:** al retroceder la cámara no gira (0°); en una media vuelta esprintando el cuerpo gira
  173° progresivamente (máximo 5° por muestra) y la cámara no se mueve.
- **Cómo se implementó:** se reescribió el 27-sep a partir de una cámara lateral 2.5D; el 2026-10-02
  pasó de seguir el `forward` del jugador a orbital (el seguimiento del cuerpo causaba el giro en
  círculos con S esprintando). Se quitaron del prefab los campos huérfanos de la cámara 2.5D.
- **Falta:** shake, encuadre de combate, zoom por contexto y stick de gamepad.

### F22 — Input 🟡
- **Archivos:** `Player/PlayerInputHandler.cs`, `Core/Interfaces/IInputProvider.cs`.
- **Cómo funciona:** lee `Keyboard.current` en `Update` y bufferiza los triggers, con los
  bindings del GDD §14 (P1): WASD/flechas, Shift (sprint mantenido), Espacio, J, K, L (mantener),
  Q y C (slide). Hay `InputActionReference` opcionales (incluido Sprint), todos vacíos. "Last input
  wins" si se presionan direcciones opuestas. No hay asset `.inputactions`.
- **Falta:** un asset `.inputactions` con gamepad (P6) y las acciones Interact (E), Look y Pause.

### F23 — Animación y contacto físico 🟡
- **Objetivo:** que el personaje se vea correctamente, anime cada acción y tenga contacto creíble
  con el entorno (GDD pilar 2; el riesgo §28 pide reutilizar animaciones).
- **Archivos:** `Player/PlayerAnimator.cs`, `Player/PlayerAnimatorIds.cs`, `Player/PlayerAnimatorIK.cs`,
  `Player/PlayerContactIK.cs`, `Player/ParkourTimings.cs`, `Player/IParkourAnimationProgress.cs`,
  `Characters/Player/PlayerAnimator.controller`, `Characters/Player/Textures/`,
  `Characters/Player/Animations/`, `Editor/PlayerAnimationSetup.cs`.
- **Cómo funciona:** ver `arquitectura.md` §5.10. La FSM avisa cada cambio de estado y
  `PlayerAnimator` hace cross-fade al estado equivalente del Animator; `Speed` mueve el blend
  WalkBackward → Idle → Walk → Jog → Run → Sprint. La locomoción se reproduce en el sitio y la mueve
  el Rigidbody; el parkour usa root motion warpeado con `MatchTarget` (P22). `PlayerContactIK`
  apoya manos y pies sobre las superficies medidas. 20 estados y 5 parámetros, con IK Pass.
- **Cómo se implementó (2026-09-30):**
  - El personaje se veía sin textura porque Unity no usa las texturas embebidas en un FBX hasta
    extraerlas. Se extrajeron y el material del FBX ahora tiene Base Map y Normal Map.
  - La luz direccional de `Level-1` estaba a 0.3; se subió a 1.
  - Clips Humanoid `LowPoly` retargeteados a Ch45 y dos transiciones Ch45 (P11).
  - La validación en batch muestrea cada clip sobre Ch45 (`AnimationMode`) y comprueba que mueve el
    rig sin poses rotas.
  - Integración del parkour: 11 clips del Dynamic Parkour System (MIT) para locomoción, caída,
    aterrizaje, vault, slide y cornisa; *RunBackward* para caminar hacia atrás.
- **Cómo se corrigió el contacto (2026-10-01):** se midieron los clips muestreándolos sobre Ch45
  (posición de manos, pies y suela por frame) y se encontró: avance horneado en la pose (vault
  4.3 m, Walk/Jog hasta 2.2 m por ciclo) que sumado al movimiento del código desfasaba el cuerpo
  visible del collider; la curva `LHandCurve` vacía; el modelo 11 cm sobre el suelo; el salto con la
  pose 0.58 m sobre el collider. Se corrigió con la política de root motion por clip, `MatchTarget`,
  `PlayerContactIK` y la colocación medida del modelo (detalle en §5).
- **Falta:** clip propio de patada y una caminata hacia atrás real (T17); hit y muerte (con
  F19/F29); calibrar Run/Sprint contra la zancada (T16); confirmar la licencia de `LowPoly` (T15).

### F31 — Auto step y step down ✅
- **Objetivo:** que los escalones y bordillos bajos no detengan la carrera ni conviertan cada
  peldaño en una caída (movimiento base del Dynamic Parkour System; no es una mecánica del GDD).
- **Archivos:** `PlayerMovement.cs` (`TryAutoStep`, `TryStepDown`, `StepHeight`, `stepLayer`,
  evento `Stepped`), `PlayerGroundedStates.cs` (`Run.PhysicsUpdate`), `GroundChecker.cs`,
  `PlayerAnimator.cs` (suavizado del modelo).
- **Cómo funciona:** en `Run`, si un rayo a 5 cm del suelo choca en la dirección de movimiento, otro
  a `StepHeight` (0.4 m) no choca y la cima está a menos de 0.4 m, sube el cuerpo hasta la cima.
  Ignora pendientes y lo que sea más alto (muros, obstáculos de vault). Al bajar, si el suelo queda
  hasta 0.4 m más abajo, el cuerpo baja con él (step down), salvo durante 0.3 s después de subir un
  escalón. El `GroundChecker` usa un sphere cast (la huella de los pies), así que el borde de un
  escalón cuenta como suelo. El modelo sigue al cuerpo suavizado en 0.1 s y el IK apoya cada pie en
  su peldaño.
- **Probado:** sube bordillos de 0.15, 0.25 y 0.35 m sin detenerse ni caer, y baja la escalera
  pisando cada escalón sin pasar a `Fall`, con los pies sin atravesar los peldaños.
- **Cómo se implementó:** adaptado de `AutoStep` del DPS el 2026-09-30 (P17); step down, sphere cast
  y suavizado el 2026-10-01 (P23).

### F32 — Parkour Test Area y pruebas automáticas ✅
- **Objetivo:** probar cada movimiento y cada obstáculo estándar por separado y combinados, a mano y
  automáticamente.
- **Archivos:** `Editor/ParkourTestCircuitBuilder.cs`, `Editor/ParkourPlayModeTest.cs`,
  `Assets/Tests/ParkourTestArea/Materials/Losa.mat`, objeto `ParkourTestArea` de `Level-1`.
- **Área** (P24, P25): `Level-1` completo. Suelo plano (top en y = 0) de 66 × 80 m rodeado por
  barreras estándar de 1.5 m. El `Spawner` está en la entrada (0, 1.2, 8), mirando hacia las
  secciones. Nueve carriles paralelos que empiezan en z = 0 y avanzan hacia −Z, **sin textos**.
  Todos los obstáculos son instancias de los prefabs estándar (F33); las escaleras, las plataformas
  de caída y los pilares son fixtures simples:

  | Sección | x | Contenido (z de la cara frontal) | Qué se prueba |
  |---|---|---|---|
  | 01 Locomoción | −25 (8 m de ancho) | `Step` de 0.15 / 0.25 / 0.35 m (−8, −11, −14) · 4 pilares en zigzag (−20…−35) · escalera a una plataforma de 1 m y escalera de bajada (−40…−49) | Arranque, frenado, giros, caminar hacia atrás, auto step y step down. El pasillo libre en x = −31 sirve para correr en recto. |
  | 02 Vault bajo | −16 | `LowVault` 0.6 m (−8) | Corriendo, a un lado, en ángulo, esprintando, desde parado |
  | 03 Vault medio | −10 | `MediumVault` 1.0 m (−8) · `MediumVault` de 1.4 m de fondo (−20) | Igual, más el fondo máximo |
  | 04 Vault alto | −4 | `HighVault` 1.2 m (−8) | Igual, más el caso sin espacio para el despegue (y un obstáculo de 1.6 m que la prueba crea y borra) |
  | 05 Slide | 2 | `Slide` (−9.5) · `Slide` de 4 m de fondo como túnel (−20…−24) | C esprintando |
  | 06 Cornisa | 8 | `Ledge` 2.2 m (−8) · `Ledge` girado 30° (−20) | Agarre desde parado, corriendo, a un lado y en ángulo; colgarse, soltarse y subir |
  | 07 Muro de escalada | 14 | `ClimbWall` 3.0 m (−8) · `Ledge` de 11 m de fondo (terraza, −20) con otro `Ledge` encima (hasta 4.4 m, −24) · escalera de bajada (−31) | Salto + agarre en el aire, escalada encadenada, caída a la terraza y bajada |
  | 08 Salto / aterrizaje | 20 | Escalera a `JumpGap` (plataformas de 1 m, hueco de 2 m, −8…−18) · escalera a una plataforma de 2 m (−22…−29) · escalera a una de 3 m (−33…−41) | Saltar el hueco y aterrizajes ligero, medio y fuerte |
  | 09 Combinado | 26 | `Combined`: Step (−6) → MediumVault (−12) → Slide (−20.5) → Ledge de 3 m de fondo (−30) → LowVault (−42) → Mantle (−50) | Todo en una sola carrera |
  | 10 Laboratorio de fluidez | 32 | `LowVault` (−14) y pista libre detrás | Slide hacia un obstáculo (se detiene o encadena el vault), frenadas, giros y slides a distintas velocidades: para mirar peso, contacto, transición y recuperación |
  | 11 Mantle | 38 | `Mantle` 1.3 m (−8) · 0.9 m (−18) · 1.5 m (−28) | Subirse desde parado y corriendo, en todo el rango |

  Regenerar: **Tools → Warrior Woke → Construir Parkour Test Area** (valida que no quede ningún
  collider fuera del área y que cada obstáculo cumpla el estándar).
- **Prueba automática** (**Probar Personaje en Play Mode**): entra a Play Mode, agrega un teclado
  virtual del Input System (pasa por `PlayerInputHandler` igual que el teclado real) y recorre las
  once secciones. Cada obstáculo estándar se prueba **desde varias posiciones**: centro, 1.2 m a un
  lado, en ángulo de 20–30°, parado, corriendo y esprintando; y una comprobación de **consistencia**
  exige que el mismo tipo dé el mismo resultado desde todas las posiciones corriendo. Además del
  flujo de estados, **mide el contacto sobre el esqueleto animado**: suelas en el suelo (de pie,
  corriendo, en escalones, al aterrizar y después de cada vault), mano del vault en su punto, pies
  que no atraviesan el obstáculo, cuerpo alineado con la cara, ambas manos en el borde, manos y pies
  fuera del muro, cuerpo de frente al muro, manos en el borde al empezar a subir y sin atravesar la
  cima, cuerpo visible pegado al collider en el aire, cabeza bajo la barra y el túnel, peso del
  aterrizaje e inercia. **Calidad de movimiento (P27):** giro sin patinar e inclinación del torso;
  sprint → frenar; caminar hacia atrás → parar; vault sin teleport (velocidad entre muestras
  < 13 m/s), con la misma velocidad de entrada y salida, saliendo a la velocidad del clip y sin
  inclinación procedural; caminando → vault bajo; fuera de alcance → salto; sprint → slide → correr,
  → parar y → salto; correr → slide (más corto que esprintando); caminar → slide (no hay); slide con
  poco espacio y slide → vault; agarre → subida → correr sin teleport. También combate, esquiva,
  bloqueo y cero errores en consola. Resultado del 2026-10-02: **301/301** en dos corridas seguidas. Cada fallo imprime el estado, la posición y la suela; el del vault indica
  además el pie, el momento del clip y la posición de la mano.
- **Consideraciones:** no prueba la cámara con ratón (no existe), enemigos ni la calidad visual de
  las poses; eso se revisa jugando (sección 10). El fallo intermitente de un pie hundido en escalones
  (T24) se resolvió en P27.

### F33 — Parkour Obstacle Standard y prefabs de obstáculos ✅
- **Objetivo:** que todos los obstáculos del juego se construyan con un molde de medidas
  consistentes que el parkour ya espera, para no recalibrar cada obstáculo al hacer los niveles.
- **Archivos:** `scripts/Parkour/ParkourStandard.cs`, `scripts/Parkour/ParkourObstacle.cs`,
  `Editor/ParkourObstaclePrefabs.cs`, `Assets/Prefabs/Parkour/` (10 prefabs + materiales).
- **Cómo funciona:** `ParkourStandard` guarda en un solo lugar las medidas de cada tipo y los
  límites de detección (los leen `EnvironmentChecker`, `PlayerLedgeGrabState` y el auto step). Los
  prefabs se generan desde él. `ParkourObstacle` mide la geometría, dice en el Inspector si cumple el
  estándar y dibuja los puntos de inicio, mano/agarre y aterrizaje. Catálogo completo, medidas y
  convención del pivote: `arquitectura.md` §5.13.
- **Catálogo:** Step 0.25 · LowVault 0.6 · MediumVault 1.0 · HighVault 1.2 · Barrier 1.5 (no
  transitable) · Ledge 2.2 · ClimbWall 3.0 · Slide (paso libre 1.2) · JumpGap (hueco de 2 m) ·
  Combined.
- **Cómo se implementó (2026-10-02, P25):** se auditaron las dimensiones del personaje y de los
  obstáculos que ya pasaban las pruebas, se eligieron alturas dentro de los rangos con margen y se
  movieron a `ParkourStandard` los valores que estaban repartidos (campos serializados de
  `EnvironmentChecker`, que el prefab ya contradecía en el radio del pre-filtro; constantes de
  `PlayerLedgeGrabState`; `StepHeight`). El Parkour Test Area se reconstruyó solo con los prefabs.
- **Consideraciones:** los prefabs no se editan a mano (se regeneran). La detección sigue midiendo
  la geometría real, así que un obstáculo fuera del estándar puede funcionar igual, pero sin la
  garantía de que caiga dentro de los rangos. Entre 1.2 y 1.9 m no hay acción (banda de la barrera).

### F34 — Marchas: caminar, strafe, retroceso rápido, agacharse ⚠️ Fuera del GDD (P28)
- **Objetivo:** moverse a distintas velocidades y en cualquier dirección sin tener que girar el
  cuerpo, con la cámara quieta.
- **Archivos:** `PlayerMovement.cs` (`WalkSpeed`, `BackpedalSpeed`, `CrouchSpeed`, `IsWalking`,
  `IsOriented`), `PlayerGroundedStates.cs` (`Run`, `PlayerCrouchState`), `PlayerInputHandler.cs`
  (Ctrl), `PlayerAnimator.cs` (`MoveX`/`MoveZ`), `Editor/ClipMeasurement.cs`.
- **Cómo funciona:** tres marchas: caminar (Ctrl mantenido, 1.7 m/s), correr (5 m/s) y sprint (Shift,
  7 m/s). Caminando, el cuerpo mira a la cámara y se mueve en cualquier dirección (strafe, diagonales,
  hacia atrás). Corriendo gira hacia donde va, salvo hacia atrás, donde corre hacia atrás mirando a la
  cámara a 3.5 m/s. C sin momentum agacha el cuerpo (collider al 62 %, 1 m/s, gira hacia donde va);
  C, Shift o Espacio lo levantan si no hay techo. El blend direccional coloca cada clip en su
  velocidad medida.
- **Probado:** caminar a 1.7 m/s; strafe a la derecha a 1.7 m/s sin girar (blend en +X); caminar hacia
  atrás a 1.7 m/s; diagonal hacia atrás a 3.5 m/s sin girar; de atrás a adelante sin girar y con la
  velocidad cambiando de sentido gradualmente; el sprint reproduce la carrera más rápido (sin
  patinar); agacharse baja el collider, camina a 1 m/s y C lo levanta; la pose siempre mira hacia
  donde mira el cuerpo.
- **Animaciones:** Walk, WalkBackward (generado), RunBackward y diagonales, strafes (LowPoly),
  Crouch_Idle/Crouch_Fwd (Quaternius).

### F35 — Mantle ⚠️ Fuera del GDD (P28)
- **Objetivo:** subirse a un bloque de 0.8–1.5 m que tiene sitio arriba, en lugar de saltarlo o
  chocar con él.
- **Archivos:** `PlayerParkourStates.cs` (`PlayerMantleState`), `EnvironmentChecker.TryFindMantle`,
  `PlayerAnimator.cs` (`MatchTarget`), `PlayerContactIK.SolveMantle`, `ParkourStandard` (tipo `Mantle`).
- **Cómo funciona:** Espacio frente al bloque: lento, mantle si hay sitio arriba; corriendo, vault si se
  puede saltar y mantle si es más alto. Corriendo, Espacio se puede pulsar antes y el mantle espera a
  su distancia. El clip *ClimbUp_1m* (Quaternius) sube 1 m; `MatchTarget` lleva la **mano de apoyo
  sobre la cima medida** (corrige la distancia y la altura, 0.8–1.5 m) y después los pies al punto de
  pie. El IK mantiene la mano en la cima mientras el clip la apoya y los pies fuera del bloque. La
  carrera sigue arriba con la velocidad del clip.
- **Probado:** bloques de 0.9, 1.3 y 1.5 m desde parado y 1.3 m corriendo con Espacio anticipado (y al
  final del recorrido combinado): la mano de apoyo queda en la cima (0 cm), los pies nunca dentro del
  bloque, de pie arriba, sin teleport (máximo 8 m/s), con la pose mirando hacia donde sube.

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
- Solo existe `Level-1.unity`, que desde el 2026-10-01 es el Parkour Test Area (F32, P24). Faltan los
  3 niveles del GDD (§10).

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
- `Rb.MovePosition` / `Rb.MoveRotation` respetan la interpolación (la rotación del Player usa
  `MoveRotation`). Para **teletransportar** (reaparición en checkpoint), usa `Rb.position`, no
  `MovePosition`. Excepción controlada: durante el parkour (`IsRootMotionDriven`) el cuerpo es
  kinemático y sin interpolación, y la animación escribe su transform en `OnAnimatorMove`;
  `EndRootMotion` devuelve el cuerpo a la física en esa pose.
- **Interpolación:** según Unity, activarla *"only if you see jitter"*. El Player la tiene activa
  desde el 2026-10-01 (la cámara en `LateUpdate` sigue un cuerpo que se mueve a 50 Hz, T7). Con
  interpolación activa, no escribas el transform de un cuerpo dinámico: usa la API del Rigidbody.
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
| 2026-10-01 | (ver `git log`) | Axel | Contacto físico del parkour (root motion + `MatchTarget` + IK), movimiento a escala humana, aterrizaje con peso, Parkour Test Area y limpieza de recursos sin uso (detalle abajo). 140/140 en Play Mode. |
| 2026-10-02 | (sin commit) | Axel | Reconstrucción del movimiento (P28), fases 2–10: animaciones CC0 de Quaternius, herramienta de medición de clips, locomoción direccional con marchas (caminar, strafe, retroceso a 3.5 m/s, agacharse), slide con bucle real, mantle, drop y salto de cornisa, roll de aterrizaje, laboratorio S11 y limpieza (detalle abajo). 341/341 en Play Mode. |
| 2026-10-02 | (sin commit) | Axel | Reconstrucción del movimiento (P28): auditoría, investigación de repositorios y licencias, arquitectura D aprobada; fase 1: cámara orbital con ratón (P5) y giro limitado por la aceleración lateral (media vuelta que frena y pivota). 304/304 en Play Mode. |
| 2026-10-02 | (sin commit) | Axel | Calidad de movimiento, segunda fase (P27): momentum en la locomoción, inclinación del torso, slide contextual, aproximación del vault, transiciones sin cambios de velocidad, T24 resuelto y laboratorio de fluidez (detalle abajo). 301/301 en Play Mode, dos corridas. |
| 2026-10-02 | (sin commit) | Axel | Parkour Obstacle Standard: estándar centralizado, 10 prefabs de obstáculos con validación y gizmos, Parkour Test Area reconstruida con ellos y pruebas de consistencia por posición (detalle abajo). 212/212 en Play Mode. |

### Detalle — reconstrucción del movimiento, fases 2–10 (2026-10-02)

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| No se podía retroceder ni hacer strafe de verdad | Blend 1D por rapidez; un trote hacia atrás ralentizado ×0.6 a 1.5 m/s | Blend 2D direccional con clips en su velocidad medida; marchas; retroceso a 3.5 m/s | ✅ |
| Los pies patinaban corriendo y esprintando (T16) | *Run* va a 5.9 m/s y *Sprint* (LowPoly) a 4.5, más lento | Velocidades medidas con `ClipMeasurement`; el sprint reproduce *Run* más rápido; `Sprint.fbx` eliminado | ✅ |
| El slide se repetía | Tramo de un slide continuo usado como bucle | Clips de Quaternius con bucle real, encadenados en seco | ✅ |
| Obstáculos de 1.2–1.5 m no tenían acción | Solo existía el vault y el agarre a partir de 1.9 m | Mantle con `MatchTarget` de la mano de apoyo sobre la cima | ✅ |
| Al correr hacia un bloque alto, Espacio saltaba contra él | La intención anticipada solo existía para el vault | La aproximación vale para mantle y vault; la intención nunca se convierte en un salto tardío | ✅ |
| Los clips de Quaternius salían de espaldas | Su rig mira a −Z; con la raíz "según el cuerpo" el giro dependía de la pose | Raíz "Original" + 180°, como LowPoly y DPS; prueba "la pose mira hacia donde mira el cuerpo" | ✅ |
| La mano del mantle se apoyaba en el aire | El clip espera el borde a ~0.55 m y el warp solo corregía la altura | `MatchTarget` de la mano sobre el punto medido de la cima | ✅ |
| Pop en el mantle alto y corriendo (13–15 m/s) | El cross-fade se comía la ventana del warp | Ventana hasta mitad del apoyo y cross-fade de 0.05 s | ✅ |
| La mano del slide se hundía en el suelo | Durante la mezcla de la entrada al bucle el IK no la sostenía | Entrada al bucle en seco (las poses coinciden) y el torso se apoya en la mano | ✅ |
| No había drop ni salto desde la cornisa | — | Drop con la subida invertida (clip generado) y salto lejos del muro | ✅ |
| Aterrizajes fuertes corriendo terminaban en una pose agachada | Un solo aterrizaje fuerte | Roll (Quaternius) que conserva el 70 % de la velocidad | ✅ |

### Detalle — calidad de movimiento (2026-10-02)

Formato del GDD §29: **problema → causa → solución → estado**. Todo con la arquitectura aprobada
(P26); sin plugins ni clips nuevos.

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| El personaje gira como robot y patina en los giros | La velocidad iba en línea recta al input mientras el cuerpo giraba aparte | La velocidad sigue la orientación; giro máximo según la rapidez; frenada antes de un giro de más de 135° | ✅ |
| Movimiento rígido, sin peso | La postura era exactamente la del clip | Inclinación procedural del torso con la aceleración real (giro, aceleración, frenada) | ✅ |
| El slide "se reinicia" | El sub-clip *Slide* se importaba como bucle sin serlo: la pose saltaba atrás cada 0.37 s | Sin bucle: se reproduce una vez y se mantiene, estirado sobre la duración prevista | ✅ |
| El slide era siempre igual y se disparaba | Duración fija de 0.8 s; solo exigía sprint | Slide contextual: momentum, superficie y espacio para entrar; termina por momentum, espacio, input o ventana; encadena vault o salto | ✅ |
| El vault aceleraba al personaje (5 → 6.2 m/s) | La reproducción usaba la velocidad media del clip, no la de su carrera | Reproducción a la velocidad de carrera del clip (5.45 m/s) | ✅ |
| Pop o pierna dentro del obstáculo en vaults altos corriendo | El vault empezaba donde se pulsaba Espacio y el cross-fade se comía la ventana de despegue | Aproximación al punto de despegue (1.2 m) y offset que respeta el cross-fade | ✅ |
| Cambio de velocidad al terminar vault y subida | Velocidad fija al salir (aprox., 4 m/s o 0) | Velocidad real del root motion | ✅ |
| Pie hundido hasta 7 cm al subir escalones (T24) | Un hueco de un tick en el ground check (auto step) apagaba el IK de pies | El IK usa el mismo margen que la FSM (`FallGraceTime`) | ✅ |

### Detalle — Parkour Obstacle Standard (2026-10-02)

Formato del GDD §29: **problema → causa → solución → estado**.

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| Cada obstáculo se construía a mano, sin medidas consistentes | No había estándar ni prefabs: el área los creaba uno a uno por código | `ParkourStandard` + 10 prefabs generados + `ParkourObstacle` que valida y dibuja los puntos de contacto (P25, F33) | ✅ |
| Los límites del parkour estaban repartidos y ya se contradecían | Campos serializados en `EnvironmentChecker` (el prefab tenía 1.2 m de pre-filtro, el código 1.4), constantes en `PlayerLedgeGrabState`, `StepHeight` serializado, medidas copiadas en el área y la prueba | Todo lee `ParkourStandard`; los campos se eliminaron del componente y del prefab; el pre-filtro queda en 1.4 m (cubre un obstáculo de 0.45 m a 1.1 m) | ✅ |
| Entre 1.2 y 1.9 m ningún movimiento funcionaba y no estaba documentado | Es el hueco entre el vault más alto y el agarre más bajo | Se documenta como banda de la `Barrier` (layer Ground, bloquea a propósito); el perímetro del área usa barreras | ✅ |
| Vault alto (1.2 m) desde parado: la pierna atravesaba la cima 14 cm | Cerca del obstáculo el clip arranca más adelante y no quedaba ventana para la fase de despegue | Desde parado o caminando, un obstáculo que necesita despegue exige ~0.91 m de espacio; si no, Espacio salta | ✅ |
| Solo se probaba cada obstáculo desde una posición | La prueba seguía un único camino por obstáculo | Varias posiciones por tipo y comprobación de consistencia entre ellas | ✅ |
| Escena de pruebas con materiales y nombres heredados | `Tests/ParkourCircuit`, `Combo.mat` | Carpeta renombrada a `Tests/ParkourTestArea`, materiales de obstáculos junto a los prefabs, `Combo.mat` eliminado (sin referencias) | ✅ |
| Escalón de bajada y caída de 2 m: un pie a −4 / −6 cm del suelo en una de cinco corridas | Variación de la física en batch, cerca de la tolerancia (3 cm) | Registrado como T24; sin cambio de código | 🟡 |

### Detalle — contacto físico del parkour (2026-10-01)

Formato del GDD §29: **problema → causa → solución → estado**. Cada causa se midió muestreando los
clips sobre Ch45 o en Play Mode; cada solución la comprueba `ParkourPlayModeTest`.

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| El vault se adelantaba al obstáculo y "regresaba" al terminar | El clip tenía 4.3 m de avance horneado en la pose y el código además movía el cuerpo hasta 2.9 m | Root motion del clip aplicado al cuerpo kinemático y warpeado con `MatchTarget` (P22) | ✅ |
| La mano del vault no se apoyaba en el obstáculo | La curva `LHandCurve` estaba vacía: el getter `ModelImporter.clipAnimations` falla en Unity 6000.6 y al reescribir los clips se perdían sus curvas (T22) | Curva restaurada desde el `.meta` original; clips editados con `SerializedObject` | ✅ |
| Las manos no tocaban la cornisa y el cuerpo quedaba en el aire | Offset fijo bajo una "esquina" medida 0.6 m por delante de la cabeza; sin orientación al muro ni IK | `TryFindLedge` (cara, normal, borde exacto, espacio arriba), `MatchTarget` de la mano, IK de manos en el borde y pies en el muro | ✅ |
| La subida no seguía la animación | Trayectoria lineal en dos fases y clip con 1.4 m de subida horneada | Root motion del clip; las manos se quedan en el borde y la raíz llega al punto de pie medido | ✅ |
| Durante el vault y el agarre el cuerpo giraba hasta 120° | `MatchTarget` sobre la mano con peso de rotación 1 orienta la muñeca, no el cuerpo | Peso de rotación 0 (como el DPS) y giro suave del cuerpo hacia la cara del obstáculo o del muro | ✅ |
| La pose del vault arrancaba 0.9 m por delante del cuerpo | Vault1 empieza a mitad de una carrera y su raíz se basaba en "Original" | Raíz de los clips con root motion basada en el centro de masa | ✅ |
| La pierna delantera atravesaba obstáculos de 1.0–1.2 m | El warp vertical se repartía hasta el apoyo de la mano y la pierna llegaba antes; además el IK de suelo seguía activo al empezar | Fase de despegue que sube lo que el obstáculo excede de 0.8 m; IK de pies sobre el obstáculo; el IK de suelo se apaga al entrar en parkour | ✅ |
| De pie, el personaje flotaba | El `Model` se centró con los bounds del SkinnedMesh, que traen margen: suelas 11 cm sobre el suelo | Modelo colocado con el vértice más bajo de la pose idle (y = −0.974) | ✅ |
| La caminata y el trote "patinaban" | Walk y Jog tenían 1.6–2.2 m de avance horneado por ciclo | Clips de locomoción en el sitio | ✅ |
| En el salto el cuerpo visible flotaba 0.58 m y caía de golpe al pasar a la caída | La subida de Jump_Up quedaba en la pose y la física también subía el cuerpo | Subida de Jump_Up como root motion no aplicado | ✅ |
| Salto de superhéroe | `JumpSpeed` 7 = 2.5 m de altura | 4.5 m/s ≈ 1 m (P23) | ✅ |
| En el aire el personaje se detenía en seco al soltar las teclas | Jump y Fall escribían la velocidad = input × velocidad | `AccelerateAir`: el input corrige el impulso | ✅ |
| Aterrizajes sin peso | Solo un clip visual que volvía a la locomoción (P19) | Severidad por altura, absorción de velocidad y recuperación, `LandHard` (P23) | ✅ |
| El slide se hundía o flotaba | `ShrinkCollider` encogía la cápsula hacia el centro del torso | El collider conserva su base; slide en tres fases; fricción en lugar de 9 m/s constantes | ✅ |
| Pies dentro del suelo al aterrizar y al correr (Run hasta 16 cm) | Sin IK de pies | `PlayerContactIK`: pies sobre el terreno, sin penetración, pelvis que baja en desniveles | ✅ |
| Cada peldaño de una escalera era una caída | No había step down y el `GroundChecker` era un solo rayo bajo el centro | `TryStepDown`, sphere cast y suavizado del modelo | ✅ |
| Jitter del cuerpo y la cámara (T7) | Rigidbody sin interpolación y rotación escrita en el transform | Interpolate + `MoveRotation` | ✅ |
| Escena de pruebas sucia | Carteles de texto, blockout anterior con objetos flotando (T19–T21) | Parkour Test Area limpio con siete secciones (P24) | ✅ |
| Recursos sin uso | `HumanPlayer.prefab`, su controller, `ball.mat`, `metal.mat`, `Sign.mat` y 10 FBX en la raíz | Eliminados tras comprobar que nada los referencia (P8, P24) | ✅ |

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
