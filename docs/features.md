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
>
> 2026-10-07: la base de **motion matching** (MxM) existe y tiene su propia prueba en Play Mode
> (`MxMLocomotionProbe`, 50/57; detalle en §5).
>
> **2026-10-08: el jugador se mueve con un `CharacterController` y camina, corre y esprinta con
> motion matching** (P29, P30) a las velocidades del mocap (P33: 1.3 / 3.4 / 4.8 m/s, atrás 2.0).
> `ParkourPlayModeTest` da 338–341/341 entre corridas (MxM no es determinista entre escenarios; las
> tolerancias que dependen del ritmo del mocap están en `arquitectura.md` T27). Los números de las
> fichas que hablan de 5 y 7 m/s son del sistema anterior y quedan como historial.
>
> **2026-10-09 (Fase 3):** el **vault** es un clip de mocap elegido de un catálogo por obstáculo, velocidad
> y distancia, y warpeado sobre la geometría (P36); el **slide** espera a la barra si C se pulsa antes; el
> **combate desarmado** tiene la cadena jab → cross → gancho, la patada de mocap, objetivo, golpe por
> contacto, hit stop, reacción al daño y una esquiva más corta, probado sobre un muñeco de
> entrenamiento (P37). `ParkourPlayModeTest` pasó a 531 comprobaciones: 529/531 con el código final
> (los fallos son intermitentes y cambian entre corridas; F32).
>
> **2026-10-09 (fase 3 del motion matching):** un rig de **Animation Rigging** (P31) pone los pies sobre
> el terreno, bloquea el pie de apoyo y gira la cabeza hacia el objetivo. `ParkourPlayModeTest`:
> **538/538** (las 531 anteriores más la sección `Rig`). **Fase 4:** el parkour se mueve con el
> `CharacterController` (ya no se apaga; solo el obstáculo de la acción es atravesable): **540/541** en la
> corrida completa (falló el slide anticipado, intermitente; repetido dos veces pasa).

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
| F09 | Ataque ligero | §5.6 | ✅ jab → cross → gancho (P37) |
| F10 | Ataque fuerte | §5.7 | ✅ patada de mocap; el retroceso lo prueba el muñeco (enemigos pendientes, P4) |
| F11 | Combo | §5.9 | ✅ |
| F12 | Bloqueo | §5.8 | ✅ |
| F13 | Vida, daño e i-frames | §5.11 | 🟡 |
| F14 | Regeneración de vida | §5.11 | ⬜ |
| F15 | Caída mortal | §5.11–5.12 | 🟡 límite de caída (barranco); sin muerte por altura |
| F16 | Sistema de armas | §5.10, §18 | 🟡 `WeaponHolder` conectado, sin armas ni pickup |
| F17 | IA de enemigos | §5.14, §12, §21 | 🟡 2.5D, sin conectar |
| F18 | Jefes | §5.15, §13 | ⬜ |
| F19 | Checkpoints, muerte y reaparición | §5.13, §6 | 🟡 muerte y reaparición en la entrada; sin checkpoints |
| F20 | Guardado | §26 | ⬜ |
| F21 | Cámara al hombro | §15 | 🟡 orbital con ratón; sin shake ni encuadre de combate |
| F22 | Input | §14 | 🟡 lectura directa, sin gamepad |
| F23 | Animación | Pilar 2 | 🟡 quedan clips provisionales (braced hang, guardia acelerada) |
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
| Caminar / correr | "Caminar / correr" con WASD | WASD = correr a 3.4 m/s con motion matching sobre mocap: arranques, curvas y frenadas son las del actor (P29, P33) |
| Caminar hacia atrás | No lo define | S sin sprint corre hacia atrás mirando al frente a ~2 m/s (100STYLE); con Ctrl camina en cualquier dirección a 1.3 m/s (P16, P28, P34) |
| Salto | "Impulso vertical" sin valor | ~1 m de altura (escala humana) e inercia en el aire (P23) |
| Aterrizaje | No lo define | Según la altura de la caída: absorbe velocidad y la recupera sin bloquear el control (P23) |
| Parkour | Solo salto, sprint y vault (§28) | Además: ledge grab/climb (desde el suelo o en el aire), drop y salto de cornisa, mantle y slide (se conservan o se aprobaron en P2 y P28; slide con C corriendo con momentum, P27) y auto step / step down (movimiento base). El wall jump se eliminó |
| Ataque fuerte | Patada (desarmado) | Patada frontal de mocap CMU (P37) |
| Recoger arma ✔ P1 | E | No existe (E no hace nada todavía) |
| Pausa | ESC | No existe |
| Combo | J → J → K, reinicio a los 0.5 s | Jab → cross → gancho (hasta 3 ligeros) y K remata desde cualquiera de ellos; más de 0.5 s entre pulsaciones reinicia la cadena (P37) |
| Regeneración | Sí | No |
| Enemigos ✔ P4 | Arquero, guerrero ligero, guerrero pesado; 3D; zona asignada | Un `Enemy` genérico sin tipos del GDD; 2.5D; patrulla en X |
| Cámara ✔ P5 | Control libre con ratón, shake, encuadre de combate | Orbital con ratón (P5); sin shake ni encuadre de combate |
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
  suelo (antes flotaban 11 cm). El rig de pies (Animation Rigging, P31; antes el IK de `PlayerContactIK`)
  apoya cada pie en el terreno y bloquea el de apoyo.
- **Momentum y peso (2026-10-02, P27):** la velocidad sigue la orientación del cuerpo
  (`AccelerateAlongFacing`): un giro curva la carrera y pierde velocidad según lo cerrado que es, y un
  input muy por detrás frena antes de girar. El giro máximo baja de 720°/s parado a lo que permite ~0.9 g de aceleración lateral
  a la velocidad que lleva (P28). El torso se inclina con la aceleración real: hacia la curva en un giro (hasta 10°),
  adelante al acelerar y atrás al frenar. Probado: en un giro de 90° a la carrera la velocidad nunca
  se separa del cuerpo (0°, antes patinaba de lado) y el torso se inclina 10°; al frenar desde el
  sprint pasa de 7.0 a 5.7 m/s en 0.1 s y el torso se echa 6° atrás.
- **Velocidad y respuesta (P39, 2026-10-10):** el mocap se reproduce ×1.25: caminar 1.6, correr 4.25,
  sprint 6.0, hacia atrás 2.5 y agachado 1.25 m/s, con la pisada, los giros y las frenadas del actor 1.25
  veces más cortos; fuera de MxM, aceleración 14 / frenado 18 m/s², giro en 0.09 s (0.15 s esprintando) y
  recuperaciones más breves tras un aterrizaje duro (0.5 s) y un roll (0.35 s). Detalle en
  `arquitectura.md` §7.3.
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
- **Desde el 2026-10-08 (P29, P30, P33):** el cuerpo es un `CharacterController` y en `Idle` y `Run` lo
  mueve **motion matching**: `PlayerMovement` le pasa la intención (dirección relativa a la cámara,
  velocidad de la marcha, orientada o libre) y MxM elige la pose de mocap que mejor la cumple; su root
  motion mueve el cuerpo. Las velocidades son las del mocap: caminar 1.3, correr 3.4 y sprint 4.8 m/s
  (P33); hacia atrás y de lado, 100STYLE hasta ~2 m/s (P34). La aceleración, las curvas, los pivots y
  las frenadas son los del actor (correr llega al 80 % en ~1 s y frena en ~1.3 s, T27). El resto de los
  estados mueve el cuerpo con su velocidad y el Animator Controller (el motion matching se mezcla
  encima por el peso de su salida). Detalle en `arquitectura.md` §5.1 ("Motor" y "Locomoción por
  motion matching").
- **Falta:** bloquear el movimiento al morir o durante animaciones de daño (GDD).

### F02 — Sprint ✅
- **Objetivo:** +~40 % de velocidad manteniendo Shift; se cancela al recibir daño, bloquear o
  atacar (GDD §5.2).
- **Archivos:** `PlayerInputHandler.cs` (`IsSprintHeld`), `PlayerMovement.cs` (`SprintMultiplier`
  1.4, `SprintSpeed`, `CanSprint`, `CancelSprint`), `PlayerGroundedStates.cs` (`PlayerRunState`).
- **Cómo funciona:** en `Run`, `IsSprint = CanSprint` en cada tick: Shift mantenido y sprint no
  cancelado. Velocidad = `BaseSpeed × 1.4` (5 → 7 m/s). Atacar, bloquear o recibir daño
  (`HealthSystem.OnDamageReceived`) llaman `CancelSprint()` y el sprint no vuelve hasta soltar Shift.
  **Se conserva** en `Jump`, `Fall` y `Vault` (GDD §5.2, §5.4). Desde el 2026-10-08 el sprint es el del
  mocap: `SprintMultiplier` 1.41 sobre 3.4 m/s = 4.8 m/s (P33), y lo anima motion matching (las tomas
  de sprint van de 4.4 a 5.4 m/s; un regulador frena las más rápidas, T27).
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
  Forward (DPS). El rig de pies (P31) se aplica en el mismo frame del aterrizaje, así los pies no se hunden.
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
- **Archivos:** `PlayerParkourStates.cs` (`PlayerVaultState`), `Parkour/VaultPlanner.cs`,
  `Parkour/VaultCatalog.cs`, `Assets/Data/Parkour/VaultCatalog.asset`, `EnvironmentChecker.cs`
  (`TryFindVault`, `TryFindLanding`), `VaultInfo.cs`, `PlayerAnimator.cs`, `PlayerContactIK.cs`,
  `PlayerGroundedStates.cs` (`Idle` y `Run`), `Editor/VaultCatalogBuilder.cs`.
- **Cómo funciona (desde el 2026-10-09, P36):** Espacio en `Idle` o `Run` busca delante un obstáculo
  de 0.45–1.1 m de alto y hasta 1.4 m de fondo (a ~1.8 m parado; corriendo, más lejos). El planificador
  elige, entre los 14 vaults de mocap del catálogo (7 del Kinematica Demo y sus espejos), el que cubre
  esa altura y ese fondo con un warp acotado, acepta la velocidad de la aproximación, cabe en la
  distancia (ni lejos ni tan cerca que la pierna choque) y lleva el mismo pie adelantado que el cuerpo.
  Si ninguno encaja, Espacio hace mantle, cornisa o salto: nunca un vault forzado. Corriendo, si el
  obstáculo está más lejos que el punto de entrada del clip, la carrera guarda la intención hasta 0.8 s y
  el vault empieza ahí. El warper lleva el cuerpo por la trayectoria medida del clip: ajusta la zancada
  de la carrera de entrada para que la mano caiga en su punto, sube o baja el cuerpo lo justo sobre la
  cima, reparte el fondo extra en el vuelo, baja los pies al suelo medido detrás y sale con la velocidad
  que trae. Las palmas se apoyan con IK en sus puntos (cada una se suelta cuando el brazo de Ch45 ya no
  alcanza) y los pies nunca entran en la cima. Detalle: `arquitectura.md` §5.17.
- **Animaciones:** vaults de mocap del Kinematica Demo (Unity Companion License, `ThirdParty/Kinematica`):
  rápidos de una mano (caminando, corriendo, esprintando), lentos con las manos y la cadera sobre la cima
  (parado o caminando, corriendo) y dos dives; cada uno con su espejo.
- **Probado (2026-10-09, secciones 02–04 y obstáculos temporales):** medio desde parado, caminando,
  corriendo, con Espacio anticipado (3.2 m) y tardío (1.4 m: el dive que despega cerca), a un lado y en
  ángulo; bajo y alto; profundo esprintando. La mano queda a 0–6 cm de su punto; ni pies, rodillas,
  cadera ni manos entran en el obstáculo; despega a la distancia de su clip (0.56 m parado, 1.56 m
  corriendo); sin teleport; aterriza con los pies en el suelo, alineado con el obstáculo, y sale a la
  velocidad que traía, sin acelerón, siguiendo la carrera. Cada tipo da el mismo resultado desde todas
  las posiciones corriendo. **Sin vault, por diseño:** alto desde parado, medio pegado (0.5 m, sin
  carrera), desde parado a 3 m (más de dos pasos), 1.2 m de alto, medio de 0.6 m de fondo caminando y con
  un muro detrás (aterrizaje bloqueado); en todos nada atraviesa el obstáculo. Storyboards del juego real
  en `Logs/PlayModeVaults/`.
- **Cómo se implementó:** el 2026-09-30 se adaptó `VaultObstacle` del DPS (P15, P17); el 2026-10-01
  pasó a root motion con `MatchTarget` (P22); el 2026-10-02 se agregó la aproximación al punto de
  despegue (P27); el 2026-10-08/09 se reemplazó por el catálogo de mocap y el warper propio (P36): el clip
  del DPS volaba en cámara lenta (~4.2 m/s²), aterrizaba a ~2.6 m y era uno solo para todo (detalle en §5).
- **Consideraciones:** durante el vault el `CharacterController` mueve el cuerpo y solo deja pasar el
  obstáculo (desde la fase 4, `arquitectura.md` §5.1); la cara, la cima, el fondo y el aterrizaje se
  comprueban antes de empezar. Cada vault conserva la gravedad de su clip: el lento
  desde parado "vuela" a ~3.6 m/s² porque la cadera se desliza sobre la cima (no es un salto), los de
  carrera a 6–10 m/s². Un obstáculo más alto o más profundo que lo que cubre el catálogo para esa marcha
  no se vaultea (`MediumVault` estándar de 0.3 m de fondo, aprobado el 2026-10-08).


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
- **Manos en el borde (2026-10-10, `arquitectura.md` §7.3):** al agarrarse se fijan dos agarres sobre
  el borde, donde cada mano animada lo cruza, a 0.35–0.75 m uno del otro y lejos de los extremos; no se
  recalculan (las manos no se deslizan) y una cornisa más angosta que los dos agarres no se agarra. Sobre
  la pose final la palma mira al borde y los dedos lo abrazan. Probado en 2.0, 2.2 (de frente, a un lado,
  girada 30°), 2.6 y 3.0 m: manos a ≤ 3.4 cm de su agarre, 55–56 cm entre ellas, dedos a ±1 cm de la cima,
  0.6 cm de deslizamiento como máximo; la de 0.5 m de ancho se rechaza. Imagen: `Logs/PlayModeLedges/agarre.png`.
- **Nota:** el GDD §28 limita el parkour a salto, sprint y vault, pero el equipo decidió
  **conservarlo activo** (P2). Solo hay braced hang: sin muro bajo el borde, los pies cuelgan (T17).

### F07 — Slide ⚠️ Fuera del GDD
- **Archivos:** `PlayerGroundedStates.cs` (`PlayerSlideState`), `PlayerMovement.ShrinkCollider`.
- **Desde el 2026-10-08 (P33):** con las velocidades del mocap el slide entra desde 2.7 m/s (correr ya
  desliza), pierde 2.5 m/s² hasta 1.8 m/s (~1.8 m desde la carrera, ~3–4 m desde el sprint), sale en la
  dirección del input (no la de la cadera del mocap, que oscila ±10°), con Espacio en cola no frena ante
  el obstáculo y sigue hasta el despegue del vault, y el cuerpo encogido no usa step offset (el barrido
  de subida del `CharacterController` chocaba con la barra). C se pulsa a ≤ 1 m del obstáculo
  (`ParkourStandard.SlideEntryDistance`).
- **Anticipación (2026-10-08, Fase 3):** C pulsado antes de una barra que el slide todavía no alcanzaría
  (su alcance es la distancia que recorre antes de agotar la inercia) queda como intención hasta 0.8 s,
  como Espacio para el vault, y el slide empieza donde lo lleva bajo la barra. Sin altura libre (techo a
  0.6 m) C no desliza: agacha. Probado: C 3.5 m antes de la barra esprintando empieza en el acto y pasa
  bajo ella; C 3.0 m antes corriendo espera hasta 1.72 m y pasa; bajo un techo de 0.6 m se agacha y
  nada del cuerpo entra bajo él.
- **Cómo funcionaba (contextual desde el 2026-10-02, P27):** C en `Run` si hay momentum (≥ 3.9 m/s),
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
  última esquiva), o desde la recuperación de un ataque. La dirección es el input de movimiento relativo
  a la cámara en ese momento (sin input, hacia el frente) y no cambia durante la esquiva. Desde el
  2026-10-09 (P37) es un roll (3 m desde el 2026-10-10, P39) que empieza rápido y frena hasta detenerse en 0.5 s
  (`v = v0 · (1 − (t/T)²)`), en lugar de un dash de 6 m a 12 m/s constantes; los muros lo detienen.
  `ActivateIFrames(0.2)`. El cuerpo no gira, así que `LocalDirection` elige el roll (adelante, atrás,
  izquierda, derecha) en el blend tree 2D `Dodge`. Pasada la invulnerabilidad (desde 0.25 s), J o K responden
  con un ataque ("evitar un ataque y responder inmediatamente", GDD §5.5). Luego va a `Run` o `Idle`.
- **Probado:** Q + S retrocede 2.1 m (dirección local (0, −1)); después de la esquiva, J entra al ataque.
- **Consideraciones:** no se puede esquivar durante el golpe de un ataque ni durante una reacción al
  daño (no tienen esa transición).

### F09 — Ataque ligero ✅
- **Objetivo:** golpe (J), daño 10 desarmado, hasta 3 encadenados, ~0.25 s entre ataques (GDD §5.6).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerAttackState`, `PlayerLightAttackState`),
  `CombatTimings.cs`, `ICombatAnimation.cs`, `PlayerAnimator.cs`, `PlayerMovement.cs` (motor),
  `Core/Combat/Hitbox.cs`, `Core/Combat/WeaponHolder.cs`.
- **Cómo funciona (desde el 2026-10-09, P37):** J encadena **jab → cross → gancho** (Quaternius, Quaternius
  y mocap CMU). Cada golpe sigue las fases medidas de su clip (`CombatTimings`): anticipación, golpe y
  recuperación. Al empezar, el cuerpo gira hacia el objetivo más cercano a ±60° del input (o del frente)
  y, si está a un paso, se acerca hasta que el puño llegue extendido a él; sin objetivo no se lanza. El
  daño lo hace el **hueso del puño** que barre cada frame (contacto real, una vez por objetivo), y al
  conectar el atacante se congela 0.06 s (hit stop). Las pulsaciones durante un golpe quedan en cola
  y el siguiente empieza al abrir la cadena (cada ~0.25 s); más de 0.5 s entre pulsaciones reinicia la
  cadena en el jab. Moverse, esquivar o bloquear interrumpen solo la recuperación. Detalle:
  `arquitectura.md` §5.4.
- **Probado (S12, muñeco de entrenamiento):** J, J, J da jab, cross y gancho, los tres conectan con 10 de
  daño, primer impacto a ~0.18 s e impactos cada ~0.3 s; el atacante se congela al conectar; el muñeco se
  inclina ~3° y se recupera; el cuerpo nunca entra en el muñeco y los puños no lo atraviesan; sin
  teleport ni suelas hundidas; después del gancho vuelve a la locomoción. Girado 45°, el golpe se orienta
  al muñeco (1–2° de error); desde 1.6 m el jab da un paso y conecta; desde 3.5 m no conecta ni se lanza
  (0.02 m); con W mantenido el jab conecta y la carrera lo interrumpe en la recuperación (0.55 s); con
  más de 0.5 s entre pulsaciones vuelve al jab.
- **Animación:** `LightAttack1` (Punch_Jab, ×1.15), `LightAttack2` (Punch_Cross, ×1.25), `LightAttack3`
  (Punch_Hook de CMU, ×1.35, entra en el 15 % del clip y con Foot IK).
- **Cómo se implementó:** impulso, giro, sincronización del golpe y buffer el 2026-09-30 (P18); fases
  medidas, cadena de tres golpes distintos, objetivo, golpe por contacto y hit stop el 2026-10-09 (P37).

### F10 — Ataque fuerte ✅
- **Objetivo:** patada (K), daño 20 desarmado, 0.8 s, retroceso, vulnerable si falla (GDD §5.7).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerHeavyAttackState`), `CombatTimings.cs`,
  `Core/Combat/TrainingDummy.cs`.
- **Cómo funciona:** K. Una **patada frontal de mocap CMU** (sujeto 135): peso atrás, rodilla arriba,
  patada y bajada a la guardia, con un paso adelante de 0.37 m hasta el impacto que es root motion (lo
  aplica el motor, escalado si el objetivo está cerca). Impacta a los 0.47 s con el **pie** y hace 20; se
  puede mover a los 0.77 s si conectó, pero si falla no se interrumpe hasta los 0.88 s (queda expuesto).
  El retroceso lo hace quien recibe el golpe: el muñeco retrocede 0.35 m con 20 o más de daño.
- **Probado:** como remate del combo conecta con 20 y el muñeco retrocede 0.36 m y vuelve; el pie no
  entra en el muñeco más de 13 cm (el muñeco cede al impacto); desde 2.0 m avanza y conecta; fallada,
  con W mantenido, dura 0.90 s sin cancelarse.
- **Falta:** el retroceso de enemigos reales (no existen, P4) y la patada con arma (F16).

### F11 — Combo ✅
- **Objetivo:** J → J → K, reinicio si pasan más de 0.5 s entre inputs (GDD §5.9).
- **Cómo funciona:** K en la ventana de cualquier golpe de la cadena (también pulsado durante el golpe:
  queda en cola) cierra con la patada. Probado: J, J, K da jab, cross y patada (10, 10 y 20). Con arma
  (F16) el combo usará las animaciones del arma.

### F12 — Bloqueo ✅
- **Objetivo:** mantener L, −70 % de daño **solo frontal**, reduce la movilidad (GDD §5.8).
- **Archivos:** `PlayerCombatStates.cs` (`PlayerBlockState`), `Core/Interfaces/IDamageModifier.cs`,
  `HealthSystem.cs`, `PlayerMovement.cs`.
- **Cómo funciona:** mantener L inmoviliza al personaje y cancela el sprint. Mientras bloquea,
  `HealthSystem.TakeDamage` pasa el daño por `IDamageModifier` (`PlayerMovement` →
  `PlayerBlockState.ModifyIncomingDamage`): si la fuente está a ±60° del frente, recibe el 30 %
  (redondeado); por la espalda o los lados, el daño completo. J mientras bloquea contraataca.
  Animación: transición Ch45 a guardia → BlockingLoop → transición Ch45 de vuelta.
- **Cómo se implementó:** 2026-09-30 (resuelve T4) con el diseño de `arquitectura.md` §7. Desde el
  2026-10-09 (P37) un golpe por la espalda, que no se bloquea, rompe la guardia con la reacción al daño
  (F13); el frontal la mantiene.
- **Probado:** golpe frontal de 20 quita 6 y la guardia aguanta; por la espalda quita 20 y reacciona.
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
- **Reacción al daño (2026-10-09, P37):** un golpe en el suelo interrumpe lo que hace el jugador
  (`PlayerHurtState`): gira hacia el golpe, retrocede ~0.25 m y reproduce `Hit_Chest` (o `Hit_Head` con
  20 o más, Quaternius). No puede moverse, atacar ni esquivar hasta que termina (GDD §5.5). En el aire,
  agachado, en el slide o en una acción de parkour la acción sigue. Probado: golpe de 15 → reacción,
  empujón de ~0.3 m, Q no esquiva durante ella, vuelve a Idle; golpe de 25 → reacción a la cabeza.
- **Consideraciones:** está en el Player.prefab (i-frames 0.5 s, GDD), en el Enemy.prefab (0.2 s) y en
  el muñeco de entrenamiento (0.1 s), por debajo de la cadencia del combo. **Nadie escucha `OnDeath` del
  jugador**, así que morir no tiene efecto. No hay HUD ni barra de vida (GDD §16, P7).
- **Falta:** regeneración (F14), muerte y reaparición (F19), feedback (F29).

### F14 — Regeneración de vida ⬜
- **Objetivo:** regenerar automáticamente tras unos segundos sin daño (GDD §5.11).
- **Plan:** componente `HealthRegen` (ver `arquitectura.md` §7). El GDD no define el tiempo ni la
  tasa; hay que acordarlos en equipo.

### F15 — Caída mortal 🟡
- **Objetivo:** muerte instantánea al caer desde gran altura o en un barranco (GDD §5.11, §5.12).
- **Hecho (2026-10-10, P38):** `Game/KillZone.cs`, un trigger que llama `HealthSystem.InstantKill()` a lo
  que entra; el área de pruebas tiene uno 6 m bajo el suelo (`LimiteDeCaida`), que reemplaza al perímetro.
  Probado: caminar por el borde mata y el jugador reaparece en la entrada con la vida completa.
- **Falta:** la muerte por altura de caída sin barranco (el GDD no define la altura; la comprobación iría
  en el aterrizaje de `PlayerFallState`, F03).

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

### F17 — IA de enemigos ✅ (P40, 2026-10-10)
- **Objetivo:** arquero (50 HP, flechas de 10, distancia), guerrero ligero (80, cortes de 12,
  rápido, esquiva) y guerrero pesado (150, golpes de 25, lento, bloquea, embiste). FSM
  `Idle → Detectar → Acercarse → Atacar → Defenderse → Buscar → Regresar`, sin salir de su zona
  (GDD §5.14, §12, §21).
- **Archivos:** `Enemy/` (`Enemy`, `EnemyPerception`, `EnemyCoordinator`, `EnemyWeapon`, `EnemyAnimator`,
  `EnemyRootMotion`, `Arrow`, `EnemyZone`), `Enemy/StateMachine/States/EnemyStates.cs`,
  `Core/Combat/EnemyData.cs`, `Editor/EnemySetup.cs` (prefabs, controller y datos medidos),
  `Editor/ParkourTestCircuitBuilder.cs` (S13 y el NavMesh).
- **Cómo funciona:** cada enemigo es un `NavMeshAgent` con su FSM (`arquitectura.md` §5.5): ve (rango,
  ángulo, línea libre), oye al jugador que corre o pelea, investiga, persigue dentro de su zona, guarda
  su distancia y su posición alrededor del jugador, ataca **solo con turno** (2 cuerpo a cuerpo y 2
  arqueros a la vez), se defiende (bloqueo, esquiva) y castiga la recuperación del jugador. Cada ataque
  tiene anticipo, una **ventana de golpe medida en su clip** (fuera de ella el arma no hace daño), golpea
  una vez por objetivo y nunca a través de un muro, y una recuperación castigable. El ligero entra
  rápido, encadena tres cortes y se lanza con una estocada; el pesado avanza despacio, anuncia sus golpes
  (anticipo retenido), bloquea, embiste y aguanta golpes ligeros sin dejar de golpear (pero le hacen
  daño); el arquero dispara desde donde ve, no dispara si la línea está bloqueada (muros o aliados),
  busca un punto con línea libre, se aleja si se le acercan y busca puestos elevados. Reacciones: Hit,
  HitHeavy y Knockback según el golpe; muerte con animación.
- **Apariencia:** Ch45 teñido por tipo (rojo el ligero, azul el pesado, verde el arquero) con katana,
  kanabo o arco de primitivas, provisional hasta los modelos del equipo (P4). Imagen: `Logs/EnemyClips/lineup.png`.
- **Dónde:** S13 (`features.md` F32): encuentro mixto y pista de pruebas.
- **Probado (2026-10-10):** sección `Enemies` de `ParkourPlayModeTest` (resultados en `arquitectura.md` §5.5).
- **Cómo se implementó:** el `Enemy.cs` 2.5D del 29-sep (con `EnemyGroundedStates`) se reescribió desde
  cero; el `Enemy.prefab` anterior se eliminó (T2, T3). Al probar se corrigió: la IA usaba la raíz del
  jugador (centro del cuerpo) como sus pies; los ataques que avanzan en su clip quedaban cortos sin root
  motion; el alcance se medía con el punto más adelantado de la hoja, que en el golpe alto del pesado
  está a un lado (fallaba por 7 cm): ahora es la mayor distancia a la que un cuerpo justo enfrente es tocado.
- **Falta:** audio y efectos de impacto (F28, F29); modelos definitivos.

### F18 — Jefes ✅ (P40, 2026-10-10)
- **Objetivo:** el líder del clan rival (300 HP, 15–25) y el Comandante (450, 20–30); al entrar en su
  zona se bloquea la salida; sin fases (GDD §5.15, §13).
- **Archivos:** los de F17 más `Enemy/BossArena.cs`; datos `Data/Enemies/LiderClan` y `Comandante`.
- **Cómo funciona:** cada jefe tiene su arena amurallada en S14. Al entrar 2 m, la puerta se cierra y el
  jefe ataca; al morir el jefe, la puerta queda abierta; si el jugador muere dentro, la puerta se abre y
  el jefe vuelve a su puesto con la vida completa. **Líder:** presiona de cerca, abre con combos de tres
  cortes (88 % de sus aperturas), se lanza con la estocada desde lejos, bloquea, esquiva y castiga, y queda
  abierto 1.1 s tras su golpe alto. **Comandante:** más rápido y con ventanas cortas; cuenta lo que hace el
  jugador frente a él y se adapta: contra un jugador que bloquea elige golpes que rompen la guardia (del
  44 % al 94 % medido), contra uno que esquiva retiene el anticipo, contra uno que ataca mucho guarda más.
  Ninguno repite la misma apertura dos veces seguidas. Sin barras de vida.
- **Probado (2026-10-10):** sección `Bosses` de `ParkourPlayModeTest`: puertas, persecución, repertorio
  variado (4–5 ataques distintos en 16 s), defensa ante los ataques del jugador, que no salen de su
  arena, ventanas medidas, identidades y el reinicio al morir el jugador.

### F19 — Checkpoints, muerte y reaparición 🟡
- Checkpoint automático al cruzarlo (una activación). Al morir, reaparecer en el último con la vida
  completa (GDD §5.13, §6).
- **Hecho (2026-10-10):** muerte del jugador (`PlayerDeadState`: el clip `Death01` de Quaternius, sin
  control, la gravedad sigue) y reaparición a los 3 s en `PlayerMovement.RespawnPoint` con la vida
  completa (`HealthSystem.Revive`). El punto de reaparición es donde el jugador apareció
  (`SetRespawnPoint` es el gancho para los checkpoints).
- **Falta:** `Checkpoint` + `CheckpointManager` (una activación por checkpoint, autoguardado F20).

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
  `Player/PlayerContactIK.cs`, `Player/PlayerRig.cs`, `Player/Rigging/` (`GroundContactConstraint`,
  `HeadLookConstraint`), `Player/ParkourTimings.cs`, `Player/IParkourAnimationProgress.cs`,
  `Characters/Player/PlayerAnimator.controller`, `Characters/Player/Textures/`,
  `Characters/Player/Animations/`, `Editor/PlayerAnimationSetup.cs`, `Editor/PlayerRigSetup.cs`.
- **Cómo funciona:** ver `arquitectura.md` §5.10. La FSM avisa cada cambio de estado y
  `PlayerAnimator` hace cross-fade al estado equivalente del Animator; `Speed` mueve el blend
  WalkBackward → Idle → Walk → Jog → Run → Sprint. La locomoción se reproduce en el sitio y la mueve
  el motor (desde el 2026-10-08, en Idle y Run, con el root motion de motion matching mezclado
  encima, P29); el parkour usa root motion warpeado con `MatchTarget` (P22). `PlayerContactIK`
  apoya manos y pies del parkour sobre las superficies medidas, y desde el 2026-10-09 un rig de
  **Animation Rigging** (P31) pone los pies sobre el terreno, bloquea el pie de apoyo y gira la cabeza
  hacia lo que importa, sobre la pose final. 40 estados (14 de vault) y 7 parámetros, con IK Pass.
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
- **Fase 3 (2026-10-08/09):** el vault usa mocap de Kinematica elegido por obstáculo y velocidad (P36)
  y el combate clips medidos: jab y cross de Quaternius, gancho y patada de mocap CMU (con su propio
  esqueleto por sujeto y un giro que apunta el golpe al frente), reacciones al daño de Quaternius (P37).
  Salvaguardas nuevas sobre la pose final: piernas fuera de la cara del bloque en el mantle, cabeza,
  hombros, rodillas y dedos fuera de un muro, y manos fuera de un obstáculo (`arquitectura.md` §5.10).
  Revisión visual: hojas de poses de cada clip (`Logs/CombatClips`, `Logs/VaultProbe`) y storyboards del
  juego (`Logs/PlayModeVaults`, `Logs/PlayModeCombat`).
- **Animation Rigging (P31, 2026-10-09):** el rig `ContactRig` de `Model` corre sobre la pose final con
  dos constraints propios: `GroundContactConstraint` (cada pie sobre el terreno bajo el talón y bajo la
  punta, pelvis, pie de apoyo bloqueado y asentado sobre el talón o la bola del pie) y
  `HeadLookConstraint` (cabeza, cuello y pecho hacia el objetivo, hacia donde se corre o hacia la cámara).
  El de los pies trabaja sobre las metas de IK Humanoid porque el Foot IK del mocap se aplica después de
  todo y borraba un IK sobre los huesos. Con la sonda de MxM: patinaje al correr 0.124 → 0.051 m/s y
  ninguna suela ni punta bajo −0.5 cm (antes hasta −11 cm en strafe); en el jugador, las suelas de pie a
  0.0 cm (antes 1.7) y ningún pie dentro de los bordillos (`arquitectura.md` §5.10, §7.2).
- **Falta:** muerte (con F19/F29); free hang y agarre a la carrera (T17); confirmar la licencia de
  `LowPoly` (T15); el strafe a la izquierda de 100STYLE todavía patina ~0.2 m/s (T27).

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
  escalón cuenta como suelo. El modelo sigue al cuerpo suavizado en 0.1 s y el rig de pies (P31) apoya
  cada pie, talón y punta, en su peldaño.
- **Probado:** sube bordillos de 0.15, 0.25 y 0.35 m sin detenerse ni caer, y baja la escalera
  pisando cada escalón sin pasar a `Fall`, con los pies sin atravesar los peldaños.
- **Cómo se implementó:** adaptado de `AutoStep` del DPS el 2026-09-30 (P17); step down, sphere cast
  y suavizado el 2026-10-01 (P23).

### F32 — Parkour Test Area y pruebas automáticas ✅
- **Objetivo:** probar cada movimiento y cada obstáculo estándar por separado y combinados, a mano y
  automáticamente.
- **Archivos:** `Editor/ParkourTestCircuitBuilder.cs`, `Editor/ParkourPlayModeTest.cs`,
  `Assets/Tests/ParkourTestArea/Materials/Losa.mat`, objeto `ParkourTestArea` de `Level-1`.
- **Área** (P24, P25, P38): `Level-1` completo. Suelo plano (top en y = 0) de 78 × 80 m **sin
  perímetro** desde el 2026-10-10 (P38): 6 m más abajo, un límite de caída (`KillZone`) mata lo que cae
  por el borde y el jugador reaparece en la entrada. El `Spawner` está en la entrada (0, 1.2, 8), mirando
  hacia las secciones. Carriles paralelos que empiezan en z = 0 y avanzan hacia −Z, **sin textos**. Todos
  los obstáculos son instancias de los prefabs estándar (F33), cada uno con sus acciones declaradas; las
  escaleras y las plataformas de caída son fixtures simples:

  | Sección | x | Contenido (z de la cara frontal) | Qué se prueba |
  |---|---|---|---|
  | 01 Locomoción | −25 (8 m de ancho) | Escalera a una plataforma de 1 m y escalera de bajada (−40…−49). Los bordillos y los pilares se quitaron (P38) | Arranque, frenado, giros, caminar hacia atrás, auto step y step down. El pasillo libre en x = −31 sirve para correr en recto. |
  | 02 Vault bajo | −16 | `LowVault` 0.6 m (−8) | Corriendo, a un lado, en ángulo, esprintando, desde parado |
  | 03 Vault medio | −10 | `MediumVault` 1.0 m (−8) · `MediumVault` de 1.4 m de fondo (−20) | Igual, más el fondo máximo |
  | ~~04 Vault alto~~ | −4 | Eliminado el 2026-10-10 (P38): el carril queda libre y la prueba coloca ahí un `HighVault` de 1.1 m temporal | Vault alto corriendo y sin vault caminando |
  | 05 Slide | 2 | `Slide` (−9.5) · `Slide` de 4 m de fondo como túnel (−20…−24) | C esprintando |
  | 06 Cornisa | 8 | `Ledge` 2.2 m (−8) · `Ledge` girado 30° (−20) | Agarre desde parado, corriendo, a un lado y en ángulo; colgarse, soltarse y subir |
  | 07 Muro de escalada | 14 | `ClimbWall` 3.0 m (−8) · `Ledge` de 11 m de fondo (terraza, −20) con otro `Ledge` encima (hasta 4.4 m, −24) · escalera de bajada (−31) | Salto + agarre en el aire, escalada encadenada, caída a la terraza y bajada |
  | 08 Salto / aterrizaje | 20 | Escalera a `JumpGap` (plataformas de 1 m, hueco de 2 m, −8…−18) · escalera a una plataforma de 2 m (−22…−29) · escalera a una de 3 m (−33…−41) | Saltar el hueco y aterrizajes ligero, medio y fuerte |
  | 09 Combinado | 26 | `Combined`: Step (−6) → MediumVault (−12) → Slide (−20.5) → Ledge de 3 m de fondo (−30) → LowVault (−42) → Mantle (−50) | Todo en una sola carrera |
  | 10 Laboratorio de fluidez | 32 | `LowVault` (−14) y pista libre detrás | Slide hacia un obstáculo (se detiene o encadena el vault), frenadas, giros y slides a distintas velocidades: para mirar peso, contacto, transición y recuperación |
  | 11 Mantle | 38 | `Mantle` 1.3 m (−8) · 0.9 m (−18) · 1.5 m (−28) | Subirse desde parado y corriendo, en todo el rango |
  | 13 Encuentro (P40) | −14 / 25 (z −64…−100) | Encuentro mixto: dos ligeros, un pesado y dos arqueros en puestos de 1.6 m, con cobertura; al lado la pista de pruebas con un muro de 3 m | IA de enemigos (secciones `Enemies`) |
  | 14 Jefes (P40) | −14 y 24 (z −114…−142) | Dos arenas amuralladas de 28 × 28 m con puerta | Jefes (sección `Bosses`) |

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
- **Resultado de la Fase 3 (2026-10-09):** 531 comprobaciones (los vaults de cada clip del catálogo, los
  casos sin vault y el combate sobre el muñeco de S12). Las corridas completas de los últimos ajustes
  dieron 526 y 530/531, y la del código final **529/531**: los fallos cambian de una corrida a otra y
  salen de casos que dependen del ritmo del mocap
  (subir los bordillos, la rodilla contra un obstáculo sin vault, el slide anticipado, atrás → adelante;
  MxM no es determinista entre escenarios, T27).
- **Resultado de la fase 3 del motion matching (2026-10-09):** **538/538** (las 531 más 7 de la sección
  `Rig`). Con los pies del rig desaparecieron los fallos intermitentes de la Fase 3 (subir bordillos,
  atrás → adelante). **Fase 4** (parkour con el `CharacterController`, sección `ActionMotor`): **540/541**;
  el que falló fue el slide anticipado corriendo (el slide se agotó a 0.4 m de la barra), un caso
  intermitente que pasó en dos corridas más de la sección.
- **Sección `Rig` (P31, 2026-10-09):** el rig de Animation Rigging está construido; corriendo, el pie de
  apoyo se bloquea, no patina (mediana ≤ 0.24 m/s, la métrica de `MocapRetargetProbe`) y ni suelas ni puntas
  se hunden; subiendo los bordillos ningún pie atraviesa la superficie bajo él; en el aire el rig suelta los
  pies; y de pie a 50° del muñeco, la cara lo mira.
- **Sección `ActionMotor` (fase 4, 2026-10-09):** durante un vault bajo corriendo y colgado de la cornisa,
  el `CharacterController` sigue activo, solo el obstáculo de la acción es atravesable (el suelo no) y al
  terminar vuelve a ser sólido, con la cápsula y los pies en su altura.
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
- **Catálogo:** Step 0.25 · LowVault 0.6 · MediumVault 1.0 (fondo 0.3) · HighVault 1.1 · Mantle 1.3 ·
  Barrier 1.7 (no transitable) · Ledge 2.2 · ClimbWall 3.0 · Slide (paso libre 1.2) · JumpGap (hueco de
  2 m) · Combined. (2026-10-08, P36: el vault alto bajó de 1.2 a 1.1 m y el medio pasó a 0.3 m de fondo,
  lo que cubren los vaults de mocap a todas las marchas.)
- **Cómo se implementó (2026-10-02, P25):** se auditaron las dimensiones del personaje y de los
  obstáculos que ya pasaban las pruebas, se eligieron alturas dentro de los rangos con margen y se
  movieron a `ParkourStandard` los valores que estaban repartidos (campos serializados de
  `EnvironmentChecker`, que el prefab ya contradecía en el radio del pre-filtro; constantes de
  `PlayerLedgeGrabState`; `StepHeight`). El Parkour Test Area se reconstruyó solo con los prefabs.
- **Consideraciones:** los prefabs no se editan a mano (se regeneran). La detección sigue midiendo
  la geometría real, así que un obstáculo fuera del estándar puede funcionar igual, pero sin la
  garantía de que caiga dentro de los rangos. Entre 1.5 y 1.9 m no hay acción (banda de la barrera).

### F34 — Marchas: caminar, strafe, retroceso rápido, agacharse ⚠️ Fuera del GDD (P28)
- **Objetivo:** moverse a distintas velocidades y en cualquier dirección sin tener que girar el
  cuerpo, con la cámara quieta.
- **Archivos:** `PlayerMovement.cs` (`WalkSpeed`, `BackpedalSpeed`, `CrouchSpeed`, `IsWalking`,
  `IsOriented`), `PlayerGroundedStates.cs` (`Run`, `PlayerCrouchState`), `PlayerInputHandler.cs`
  (Ctrl), `PlayerAnimator.cs` (`MoveX`/`MoveZ`), `Editor/ClipMeasurement.cs`.
- **Cómo funciona:** tres marchas: caminar (Ctrl mantenido, 1.6 m/s), correr (4.25 m/s) y sprint
  (Shift, 6.0 m/s) (P39 desde el 2026-10-10; P33: 1.3 / 3.4 / 4.8; antes 1.7 / 5 / 7). Caminando, el cuerpo mira a la cámara y se mueve en
  cualquier dirección (strafe, diagonales, hacia atrás). Corriendo gira hacia donde va, salvo hacia
  atrás, donde corre hacia atrás mirando a la cámara a 2.5 m/s (P39; antes ~2 y 3.5). Desde el 2026-10-08 la
  locomoción orientada es motion matching con las tomas de 100STYLE (tag `Strafe`); el motor corrige
  su deriva de orientación (±10°) y sigue orientada hasta detenerse. Corriendo orientado se favorecen
  las tomas de carrera (`BR`/`SR`, P39) y al pasar de una marcha orientada a la libre la orientada da la
  vuelta al movimiento hasta 1 m/s en la nueva dirección (sin pivotes ni tirones).
 C sin momentum agacha el cuerpo (collider al 62 %, 1 m/s, gira hacia donde va);
  C, Shift o Espacio lo levantan si no hay techo. El blend direccional coloca cada clip en su
  velocidad medida.
- **Probado (2026-10-10, P39):** caminar a 1.58 m/s, strafe a la derecha 1.60 y hacia atrás caminando 1.59
  (de 1.6), diagonal hacia atrás corriendo 2.20–2.46 m/s (de 2.5) sin girar (≤ 3°), de atrás a adelante con
  ≤ 7° de giro. Antes (P28): caminar a 1.7 m/s; strafe a la derecha a 1.7 m/s sin girar (blend en +X); caminar hacia
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
- **Fase 3 (2026-10-08/09):** las rodillas y los dedos del pie que sube ya no entran en la cara del bloque
  (salvaguarda sobre la pose final); la palma de apoyo se fija con IK en su punto de la cima desde que se
  apoya (antes se deslizaba hacia la cima mientras terminaba el warp, y a veces seguía en el borde a mitad
  del apoyo); y el cuerpo empieza a moverse cuando motion matching ya no se ve (su deriva del idle dejaba
  la mano hasta 10 cm corta).

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
  `MovePosition`. (Desde P30 el jugador no tiene Rigidbody: es un `CharacterController` que mueve el motor
  una vez por frame, y desde la fase 4 del motion matching también el root motion del parkour pasa por
  `Move`, con el obstáculo de la acción atravesable; `arquitectura.md` §5.1.)
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
| 2026-10-05 | (ver `git log`) | Axel | Segunda auditoría de locomoción, parkour, animación y combate; decisiones P29–P32 (motion matching con MxM, `CharacterController`, Animation Rigging, repo privado). Fase 0: mocap del Kinematica Demo (22 tomas, Unity Companion License) importado como Humanoid y probado sobre Ch45 con la sonda de retarget (hoy `MocapRetargetProbe`; detalle abajo). El jugador todavía no lo usa. |
| 2026-10-05 | (ver `git log`) | Axel | Retroceso y strafe de 100STYLE (P34): 8 tomas Neutral (CC BY 4.0) convertidas de BVH a FBX con Blender, importadas como Humanoid con mapeo explícito y probadas sobre Ch45; la sonda pasa a `MocapRetargetProbe` y prueba las dos fuentes. Retarget limpio, pero Neutral solo cubre velocidades bajas (detalle abajo). El jugador todavía no lo usa. |
| 2026-10-05 | (ver `git log`) | Axel | Retroceso y strafe rápidos: búsqueda de otra fuente de mocap (ninguna libre y compatible pasa de ~2 m/s); se agregan las 8 tomas **Rushed** de 100STYLE (atrás ~2.0, de lado ~2.2 m/s) y el convertidor acepta cualquier estilo. Retarget limpio con Foot IK. |
| 2026-10-08 | (ver `git log`) | Axel | Motion matching, fase 2 (P29, P30, P33): el Player pasa de Rigidbody a **`CharacterController`** y camina, corre y esprinta con MxM mezclado sobre el Animator Controller (`PlayerMxMLocomotion`, motor en `PlayerMovement.LateUpdate`); velocidades del mocap; dos parches más en MxM (T26); parkour y prueba reajustados a las velocidades nuevas (detalle abajo). `ParkourPlayModeTest` 338–341/341 entre corridas (T27). |
| 2026-10-08/09 | (ver `git log`) | Axel | Fase 3, vault y combate (P36, P37): el vault es un clip de mocap de Kinematica elegido de un catálogo medido (`VaultCatalogBuilder`, `VaultPlanner`) y warpeado por código propio (`arquitectura.md` §5.17); el slide espera a la barra; el combate desarmado sigue fases medidas (`CombatTimings`): jab → cross → gancho y patada de mocap CMU, objetivo, golpe por contacto, hit stop, reacción al daño y esquiva más corta, probado sobre el muñeco de entrenamiento (§5.4). Correcciones encontradas al probar: las acciones ya no reciben el root motion de MxM mientras se desvanece, la palma del mantle se fija al apoyarse, salvaguardas de rodillas, dedos y manos contra muros, step offset 0 solo en el aire o sobre una arista sin apoyo. Detalle en F04, F09–F12, F23 y F32. `ParkourPlayModeTest`: 531 comprobaciones, 529/531 con el código final (fallos intermitentes distintos en cada corrida, F32). |
| 2026-10-09 | (ver `git log`) | Axel | Motion matching, fase 3 (P31): **Animation Rigging** 6.6.0 con dos constraints propios sobre la pose final (`GroundContactConstraint`: terreno bajo talón y punta, pelvis, pie de apoyo bloqueado y asentado; `HeadLookConstraint`: cabeza, cuello y pecho), armados por `PlayerRigSetup` y gobernados por `PlayerRig`; el IK de suelo sale de `PlayerContactIK`. Los pies trabajan sobre las metas de IK Humanoid porque el Foot IK del mocap se aplica después de todo (detalle abajo). `MxMLocomotionProbe`: suelas resueltas en todos los escenarios y patinaje menor en todos menos el strafe a la izquierda; `ParkourPlayModeTest` **538/538** con la sección `Rig` nueva. |
| 2026-10-09/10 | (ver `git log`) | Axel | Motion matching, fase 4 (P30): el root motion del parkour (vault, agarre, subida, mantle, drop) se aplica con `CharacterController.Move` en lugar de apagar el controller; solo el obstáculo de la acción se deja atravesar (`Physics.IgnoreCollision` con los colliders que mide `EnvironmentChecker`) y la cápsula se reduce a torso y cabeza mientras dura (detalle abajo). Sección `ActionMotor` nueva; `ParkourPlayModeTest` **540/541** (el slide anticipado, intermitente). Con esto el plan de §7.2 quedó completo. |
| 2026-10-10 | `567f906` | Axel | Fase de desarrollo del 2026-10-10, fases 1–2 (P38): auditoría y obstáculos: fuera pilares, bordillos, el vault alto de S04 y el perímetro; cada obstáculo declara sus acciones; límite de caída y muerte con reaparición (`arquitectura.md` §7.3). |
| 2026-10-10 | (ver `git log`) | Axel | Fase 3 (P39): mocap ×1.25 con el parche `PastScale` de MxM (T26 #5), favour tag de las carreras de 100STYLE, traspaso de marcha orientada a libre a 1 m/s, `Velocity` medida sin el sesgo de la velocidad pedida, regulador que también acelera (hasta ×1.3, nunca más de ×1.1 el sprint), frenadas libres ×1.15 y un tope que impide volver a acelerar al detenerse, respuesta fuera de MxM y **agarres fijos de las dos manos en la cornisa**. Medido: caminar 1.54, correr 4.3, sprint 6.0, atrás 2.4 m/s, se detiene en ~1 s (`arquitectura.md` §7.3). |
| 2026-10-10 | (ver `git log`) | Axel | Fases 4–8 (P40): enemigos y jefes en 3D (`arquitectura.md` §5.5, F17, F18): cinco prefabs generados por `EnemySetup` con ataques medidos, FSM, turnos de ataque, arquero, arenas de jefe; S13 y S14 en el área con NavMesh; golpes que no atraviesan muros; secciones `Enemies` y `Bosses` en la prueba. |
| 2026-10-07 | (ver `git log`) | Axel | Motion matching, fase 1 (P29): MxM 2.3.3 embebido con un parche para Unity 6.6 (T26), base de datos horneada desde código (`MxMLocomotionBuilder`: 24 118 poses de Kinematica y 100STYLE, el estilo de strafe separado por tag) y prueba en Play Mode (`MxMLocomotionProbe`). 50/57: velocidades, respuesta, giros de 180°, retroceso y patinaje cumplen; quedan suelas hundidas en giros de 90° y strafe, frenado lento y strafe derecho lento (T27). La base va por Git LFS. El jugador todavía no la usa (detalle abajo). |
| 2026-10-02 | (sin commit) | Axel | Reconstrucción del movimiento (P28), fases 2–10: animaciones CC0 de Quaternius, herramienta de medición de clips, locomoción direccional con marchas (caminar, strafe, retroceso a 3.5 m/s, agacharse), slide con bucle real, mantle, drop y salto de cornisa, roll de aterrizaje, laboratorio S11 y limpieza (detalle abajo). 341/341 en Play Mode. |
| 2026-10-02 | (sin commit) | Axel | Reconstrucción del movimiento (P28): auditoría, investigación de repositorios y licencias, arquitectura D aprobada; fase 1: cámara orbital con ratón (P5) y giro limitado por la aceleración lateral (media vuelta que frena y pivota). 304/304 en Play Mode. |
| 2026-10-02 | (sin commit) | Axel | Calidad de movimiento, segunda fase (P27): momentum en la locomoción, inclinación del torso, slide contextual, aproximación del vault, transiciones sin cambios de velocidad, T24 resuelto y laboratorio de fluidez (detalle abajo). 301/301 en Play Mode, dos corridas. |
| 2026-10-02 | (sin commit) | Axel | Parkour Obstacle Standard: estándar centralizado, 10 prefabs de obstáculos con validación y gizmos, Parkour Test Area reconstruida con ellos y pruebas de consistencia por posición (detalle abajo). 212/212 en Play Mode. |

### Detalle — motion matching, fase 4: el parkour con el `CharacterController` (2026-10-09)

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| Durante vault, agarre, subida, mantle y drop el controller se apagaba y el root motion se escribía al transform: nada detenía el cuerpo (P30, fase 2) | `MatchTarget` y el warper necesitaban la posición exacta y el cuerpo debe atravesar el obstáculo que cruza | El controller sigue encendido y el root motion se aplica con `Move`; `EnvironmentChecker` guarda los colliders de la cara y la cima (`VaultInfo` / `LedgeInfo`) y `BeginRootMotion` los deja atravesar con `Physics.IgnoreCollision` hasta `EndRootMotion` (con un `ParkourObstacle`, todos los suyos). Sin choques, `Move` deja el cuerpo exacto | ✅ |
| Los vaults bajos (0.6 m) dejaban la mano 20–33 cm sobre su apoyo | El warper baja el cuerpo hasta 0.39 m para una cima más baja que la del clip, y la cápsula entera se apoyaba en el suelo | Durante la acción la base de la cápsula sube 0.5 m (torso y cabeza siguen chocando) y `FeetY` lo descuenta | ✅ |
| ¿Se cumple de verdad? | — | Sección `ActionMotor` de `ParkourPlayModeTest`: durante un vault y colgado de una cornisa el controller sigue activo, solo el obstáculo es atravesable, el suelo no, y al terminar vuelve a ser sólido con la cápsula completa | ✅ |

### Detalle — motion matching, fase 3: Animation Rigging (2026-10-09)

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| Suelas hasta 5.5 cm bajo el suelo en el giro de 90° y 5–11 cm en strafe; el pie de apoyo patinaba 0.12–0.26 m/s (T27) | El mocap retargeteado a Ch45 y la mezcla de poses de MxM; el IK Pass del controller desaparecía mientras MxM llevaba el cuerpo (T25) | Rig de Animation Rigging (`com.unity.animation.rigging` 6.6.0) sobre la pose final con `GroundContactConstraint`: terreno bajo talón y punta, pelvis y pie de apoyo bloqueado | ✅ |
| El rig no cambiaba nada (la sonda daba lo mismo con y sin él) | El Foot IK Humanoid de los clips de mocap se resuelve después de todas las salidas del Animator y devolvía los pies a su meta | El job mueve las **metas de IK Humanoid** (pies y cuerpo) y llama a `SolveIK`; la reconstrucción del rig sobre el grafo de MxM se probó y no hacía falta | ✅ |
| Con las metas propias el patinaje subió a ~0.65 m/s | Las metas partían del pie de la pose (FK), así que el rig quitaba la corrección del Foot IK | El pie animado es la meta del Foot IK del clip (viene en el stream con peso 0) según cuánto Foot IK usa la fuente (`FootIKWeight`: MxM y los golpes de CMU) | ✅ |
| El pie bloqueado se desplazaba con el cuerpo | El cuerpo se mueve después de evaluarse la pose (root motion y motor en `LateUpdate`) | El constraint mide cada frame el movimiento posterior a la evaluación y lo predice en la siguiente | ✅ |
| Giro de 90° y strafe a la izquierda patinaban más con el bloqueo (0.23 y 0.31 m/s) | El bloqueo sujetaba el tobillo mientras el mocap pivotaba sobre la punta | Se sujeta el punto de apoyo real: el talón, o la bola del pie cuando el talón se levanta (0.06 y 0.20 m/s) | 🟡 strafe izquierda (T27) |
| Subiendo bordillos, la punta entraba 12 cm en su frente | El suelo se medía solo bajo el tobillo, todavía sobre el piso | También se mide bajo la punta (y donde estará el frame siguiente) y el pie sube a la cima que alcanza | ✅ |
| En el jugador el pie no llegaba a bloquearse (apoyo a 2.3 cm) y de pie las suelas quedaban a 1.7 cm | El Foot IK del mocap levanta el pie ~1.4 cm sobre la pose con la que se colocó el modelo | Umbral de apoyo de 4 cm y el pie bloqueado se asienta sobre el suelo: suelas a 0.0 cm | ✅ |
| La cabeza solo seguía el clip | — | `HeadLookConstraint` reparte el giro entre pecho, cuello y cabeza con límites de cuello; `PlayerRig` elige objetivo, dirección de la carrera o cámara | ✅ |

### Detalle — motion matching, fase 2: motor y locomoción en el jugador (2026-10-08)

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| ¿Cómo convivir MxM (su propio PlayableGraph) con el Animator Controller que usan las acciones, `MatchTarget` y el IK? | MxM toma el Animator con su grafo | Medido con una prueba temporal: **el peso de la salida del grafo de MxM mezcla su pose con la del controller**, que sigue corriendo debajo (peso 0 = controller, 0.5 = pose intermedia). `PlayerMxMLocomotion` sube el peso en Idle y Run y lo baja (y pausa MxM) en las acciones | ✅ |
| El personaje salía despedido (z = −828) y "teleports" de 40–66 m/s | `CharacterController.velocity` incluye el salto de un teleport; y durante la mezcla la velocidad medida (que ya incluía el root motion) volvía a sumarse como (1 − w) × velocidad: crecía como 1/w | El motor mide el desplazamiento de su propio `Move`; durante la mezcla conserva la orden de los estados; una colisión solo quita velocidad | ✅ |
| El personaje giraba solo estando quieto (180° → 211°) y tras cada acción | `MxMAnimator.ResetMotion` reinicia la orientación de la trayectoria a yaw 0 del mundo | Se reinicia con la orientación actual del cuerpo y con el pasado de su velocidad real | ✅ |
| `ArgumentNullException` y `ArgumentOutOfRangeException` dentro de MxM | Bugs de MxM al desactivar antes de `Start` y en `ForcePastTrajectoryByVelocity` | Parcheados en la copia embebida (T26) | ✅ |
| El cuerpo flotaba 5 cm | Un `CharacterController` descansa su piel sobre el suelo | `FeetY` y la colocación del modelo descuentan la piel (0.035 m) | ✅ |
| Los vaults corriendo no empezaban | Correr bajó a 3.4 m/s y el umbral de "corriendo" era 3.48 m/s | Umbral único `PlayerVaultState.IsRunning` en 2.6 m/s (clip a ×0.6 mínimo) | ✅ |
| El vault alto (1.2 m) no se detectaba en el suelo | Mide exactamente `VaultMaxHeight` y los pies leían una fracción de milímetro bajo el suelo | Tolerancia de 1 cm en vault, mantle y cornisa | ✅ |
| Parado a 0.75 m del muro de 3 m ya no se agarraba | El idle del mocap mueve la raíz 1 cm y `LedgeReachAir` era 0.75 | 0.8 m | ✅ |
| El slide chocaba con la barra de 1.2 m | Radio 0.54 (el controller nunca es más bajo que 2 × radio) y, sobre todo, **el barrido de subida del step offset** (0.4 m) de la cápsula baja | Radio 0.35 (hombros); step offset 0 con el cuerpo encogido | ✅ |
| Al levantarse junto a la barra salía disparado hacia atrás (+94 m/s) | El controller se despenetra al recuperar la altura y eso se medía como velocidad | La colisión solo quita velocidad; el techo se comprueba con la cápsula de pie completa | ✅ |
| El slide se agotaba a 0.6–1.8 m y no llegaba a la barra, al túnel ni al vault | La fricción venía de deslizarse desde 7 m/s; el balanceo del mocap lo desviaba 9° contra la pared del túnel | Fricción 2.5, mínimo 1.8, sale en la dirección del input, C a ≤ 1 m del obstáculo, y con Espacio en cola no frena y sigue hasta el despegue del vault | ✅ |
| Deriva de orientación en strafe/retroceso (hasta 40°) y residuo de rumbo en la carrera | Las tomas giran solas | El motor corrige la orientación en la locomoción orientada y el residuo (< 30°) en la libre; la orientada sigue así hasta detenerse | ✅ |
| Sprint de hasta 5.5 m/s | Tomas de sprint más rápidas que 4.8 m/s | Regulador que frena la reproducción hasta el 88 % en línea recta. El warping de velocidad de MxM se probó y se descartó (rompía las medias vueltas: desvío de 98°) | 🟡 T27 (4.4–5.4 m/s) |
| La prueba en Play Mode asumía 5/7 m/s y el blend tree | — | Tiempos, distancias y tolerancias a P33 (cada cambio comentado en `ParkourPlayModeTest`); el resultado varía entre corridas porque MxM no es determinista entre escenarios | 🟡 338–341/341 |

### Detalle — motion matching, fase 1 (2026-10-07)

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| MxM no compilaba en Unity 6.6 (`error CS0619` en `MxMAssetHandler.cs`) | Desde Unity 6.3 el cast de `int` a `EntityId` es un error | Paquete **embebido** en `Packages/` (se quitó la URL de git del manifest) con la firma `OpenAsset(EntityId, int)` bajo `UNITY_6000_3_OR_NEWER` (T26) | ✅ |
| La base de MxM se arma a mano en un inspector grande | Flujo de MxM | `MxMLocomotionBuilder` recrea el `MxMPreProcessData` desde listas en código y corre el pre-proceso; el `MxMAnimData` conserva su GUID | ✅ |
| El `MxMAnimator` de la prueba arrancaba sin datos | `EditorSceneManager.NewScene` descarga los assets sin uso y mataba la referencia cargada antes | Los datos se cargan después de crear la escena | ✅ |
| Patinaje y "saltos de pose" enormes en la primera medición | El batch corre a ~1000 fps: a 1 ms por frame un milímetro de temblor se lee como 1 m/s | Tiempo de juego fijo a 60 fps (`Time.captureFramerate`) y patinaje con la misma métrica que `MocapRetargetProbe` (comparable con el mocap original) | ✅ |
| Al correr hacia adelante MxM saltaba entre Kinematica y 100STYLE | Dos estilos de actor compitiendo en la misma búsqueda | Las tomas de 100STYLE llevan el tag `Strafe`; MxM solo busca poses con los tags requeridos exactos, así que el modo libre usa Kinematica y el strafe usa 100STYLE | ✅ |
| Pie plantado patinando al correr (0.59 m/s; el mocap solo: 0.08–0.16) | Barrido diagnóstico: la altura de la raíz venía en el root motion y el motor sostiene el cuerpo sobre el suelo, así que el cuerpo bajaba en cada apoyo; el warping angular sumaba otra parte | Las tomas de la base se importan con la altura **horneada en la pose** (basada en los pies): 0.12 m/s. El warping angular se mantiene (sin él se pierde precisión de dirección) | ✅ |
| Suelas bajo el suelo en el giro de 90° (−5.5 cm) y en strafe (−5 a −11 cm) | Strafe: desfase constante de las tomas de lado de 100STYLE (`Neutral_SR`); giro: la mezcla de tomas durante el giro | Apoyo de pies con Animation Rigging (P31, fase 3) | 📋 T27 |
| Frenado de 1.3–1.4 s (se pidió ≤ 1.2 s) | Así frena el actor en `Start_Stop_2`; una trayectoria más reactiva (25/15) no lo mejoró | Warping longitudinal en la fase del motor | 📋 T27 |
| Strafe a la derecha a 1.36 m/s | Las tomas de lado de 100STYLE promedian ~1.5 m/s y no hay espejo | Generar espejos o aceptar el tope (P34) | 📋 T27 |

### Detalle — retroceso y strafe de 100STYLE (2026-10-05)

Estilos Neutral (8 tomas) y Rushed (8 tomas).

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| 100STYLE viene en BVH (60 fps, centímetros) y Unity no importa BVH | Formato del dataset | `ThirdParty/100STYLE/bvh2fbx.py` (Blender 4.5 LTS en batch): recorta a `Frame_Cuts.csv`, pasa a 30 fps y a metros y exporta FBX; también exporta el esqueleto en reposo (`Character/Neutral_Skeleton.fbx`) para el Avatar. Solo se descarga el estilo Neutral del ZIP de 1.4 GB | ✅ |
| El mapeo Humanoid automático confundiría los huesos | En 100STYLE `Collar` es el hombro, `Shoulder` el brazo y `Hip` el muslo | Mapeo explícito en la sonda (`MocapRetargetProbe`, que ahora prueba las dos fuentes) | ✅ |
| ¿Se retargetea bien a Ch45? | — | Con Foot IK, Ch45 patina igual o menos que el esqueleto original (mediana 0.03–0.08 m/s) y las suelas quedan a ±1 cm del suelo; sin avisos de rig. Un render lateral muestra poses naturales | ✅ |
| Las hojas de poses de 100STYLE no muestran el actor | `Neutral_Skeleton` no tiene malla | Se comparan solo las métricas; la fila de abajo (Ch45) sí se ve | ⚠️ Esperado |
| Retroceso y strafe solo a velocidad baja | Neutral llega a ~0.8 m/s (atrás caminando), ~1.3 (atrás corriendo), ~0.9 (de lado caminando), ~1.6 (de lado corriendo) | Se buscó otra fuente: LaFAN1 y Bandai Namco son NC-ND, MotionPersona NC y CMU solo camina hacia atrás; Mixamo sigue bloqueado por P32. Se midieron 10 estilos de 100STYLE y se agregó **Rushed** (atrás ~2.0, de lado ~2.2 m/s; mismo esqueleto, licencia y convertidor). El retroceso y el strafe tendrán tope de ~2 m/s | ✅ |
| p90 de patinaje alto en carreras rápidas (Rushed BR/BW/SR, ~4 m/s) | El punto de apoyo salta de un pie al otro durante la fase de vuelo | Mismo patrón que los sprints de Kinematica; la mediana queda igual que el original | ⚠️ Artefacto de la métrica |

### Detalle — prueba de retarget del mocap de Kinematica (2026-10-05)

| Problema | Causa | Solución | Estado |
|---|---|---|---|
| Los clips no aceptaban el Avatar de `Unit` | `Unit.FBX` tiene `Eye_L/R` y `Jaw`, que el esqueleto de los clips no tiene | El Avatar de `Unit` solo mapea huesos presentes en los clips | ✅ |
| ¿Se retargetea bien a Ch45? | — | Mismo patinaje que el original **con Foot IK** (mediana 0.04–0.19 m/s frente a 0.04–0.15); sin Foot IK patina hasta 1.4 m/s y hunde las suelas hasta 8 cm. Hojas de poses idénticas en los dos personajes | ✅ El Foot IK es obligatorio |
| Velocidades | El mocap camina a ~1.1–1.6, trota a ~3.2 y esprinta con punta de ~4.5–5.1 m/s; el juego usa 5 (correr) y 7 (sprint) | El juego se ajusta al mocap: ~1.3 / ~3.4 / ~4.8 m/s (P33) | 📋 |
| Retroceso y strafe | Kinematica no los tiene | 100STYLE, estilos Neutral y Rushed, CC BY 4.0 (P34); hasta ~2 m/s (detalle arriba) | ✅ |
| El mocap pesa ~5 MB por toma | Tomas largas de captura continua | Git LFS solo para `ThirdParty/Kinematica` y `ThirdParty/100STYLE` (P35) | ✅ |
| Mediciones falsas al principio | `SampleAnimationClip` no aplica Foot IK, y un `PlayableGraph` evaluado a mano en el Editor deja el cuerpo en la pose de bind | La herramienta combina dos muestreos (ver `arquitectura.md` §5.9) | ✅ |

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
