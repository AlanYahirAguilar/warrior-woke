# Awakened Warrior — Contexto del proyecto

> **Documentación del proyecto.** Son tres archivos que funcionan como una sola fuente de verdad:
>
> | Documento | Qué responde |
> |---|---|
> | [`contexto.md`](contexto.md) (este) | **Qué** es el juego, qué pide el GDD, cómo abrir y trabajar el proyecto. |
> | [`arquitectura.md`](arquitectura.md) | **Cómo** está construido: sistemas, scripts, prefabs, dependencias y plan técnico. |
> | [`features.md`](features.md) | **Qué hay implementado** feature por feature, el historial de implementación y las buenas prácticas de Unity 3D. |
>
> **Fuente de diseño:** [`GDD_Awakened_Warrior.pdf`](../GDD_Awakened_Warrior.pdf) (raíz del repo, GDD final).
> Si este documento y el GDD no coinciden, manda el GDD, y hay que corregir este documento.
>
> Última revisión completa: 2026-10-02. Actualizado el 2026-10-09 (Fase 3: vault, slide y combate;
> fase 3 del motion matching: Animation Rigging).

---

## 1. Reglas para mantener esta documentación

Aplican a cualquier persona o agente que modifique el proyecto:

1. **Antes de cualquier cambio**, por pequeño que sea, lee los tres documentos, revisa el código
   relacionado y, si el cambio toca diseño o alcance, el GDD.
2. **Después del cambio**, actualiza el documento o documentos afectados:
   - Script, prefab, escena o dependencia nueva, o un cambio de estructura → `arquitectura.md`.
   - Comportamiento o estado de una feature → `features.md` (ficha de la feature + historial).
   - Alcance, controles, flujo del juego o una decisión de diseño → `contexto.md`.
3. **Consistencia:** los tres documentos no pueden contradecirse. Documenta solo lo que existe de
   verdad. Una implementación se documenta cuando ya se hizo, y la documentación de un sistema se
   borra cuando se borra el sistema.
4. **Estados** (los mismos en los tres documentos):

   | Estado | Significado |
   |---|---|
   | ✅ **Implementado** | Existe en el código **y** está conectado a prefab/escena, así que se puede probar en Play. |
   | 🟡 **Parcial** | El código existe, pero le falta conexión (prefab, assets, animaciones) o no cumple todo el GDD. |
   | 🔧 **En desarrollo** | Alguien lo está trabajando ahora mismo. |
   | 📋 **Planeado** | Hay diseño técnico acordado en `arquitectura.md`, pero no hay código. |
   | ⬜ **Pendiente** | Lo pide el GDD, pero no hay código ni diseño técnico. |
   | ⚠️ **Fuera del GDD** | Existe en el código, pero el GDD final no lo contempla o lo contradice. |
   | ⏸️ **Desactivado** | El código se conserva, pero se apagó a propósito (p. ej. una transición comentada). Se puede reactivar. |

   En `arquitectura.md` §7–§8 también se usan ✔ (decisión aprobada) y ❓ (decisión pendiente).
5. **Lo que queda fuera del GDD y no se usa se elimina**, no se desactiva (decisión P8 en
   `arquitectura.md` §8). Los IDs de features (`F…`), decisiones (`P…`) y deuda técnica (`T…`)
   **no se reutilizan**: si algo se elimina, su ID queda libre y el historial explica por qué.

## 2. Identidad del proyecto

| Elemento | Valor |
|---|---|
| Nombre del juego (GDD) | **Awakened Warrior** |
| Nombre del repositorio | `warrior-woke` (nombre heredado; el código usa `WarriorWoke` en namespaces y menús) |
| Producto en Unity | `productName: Awakened Warrior`, `companyName: SUNUX GAMES`, identificador `com.SUNUXGAMES.AwakenedWarrior` (`ProjectSettings.asset`) |
| Género | Acción 3D en tercera persona: parkour + combate cuerpo a cuerpo |
| Motor | Unity **6000.6.0f1** (Unity 6), URP |
| Plataforma | PC (Windows). La versión móvil se decide al final del proyecto. |
| Clasificación | 18+ (equivalente a PEGI 18) |
| Arte | 3D realista y cinematográfico |
| Duración de desarrollo | ~16 semanas (GDD §27) |

## 3. Concepto (GDD §1–§4)

Japón feudal, período Sengoku. **Yukimura Tadakatsu**, un joven guerrero traicionado por el
comandante de su propio clan (que también era su maestro), atraviesa un Japón en guerra total
para encontrar a su familia (padre, madre y hermana) y acabar con el clan que lo traicionó.

**Pilares de diseño:** 1) movimiento fluido, 2) animación, 3) combate cuerpo a cuerpo,
4) progresión a través de la historia, 5) niveles progresivos.

**Experiencia buscada:** fluidez en el parkour, satisfacción en el combate, sensación de progreso,
desafío creciente sin frustración y situaciones distintas en cada nivel.

**Core loop:** Avanzar → Hacer parkour → Encontrarse con enemigos → Combatir → Avanzar en la historia.

**Referencias** (solo como inspiración general): Vector (fluidez), Shadow Fight 2 (combate),
Berserk (tono y traición), Sleeping Dogs (cámara y combate), Uncharted (salud sin barra).

## 4. Mecánicas según el GDD (§5)

Esta tabla es **lo que pide el diseño**. El estado real de cada una está en
[`features.md`](features.md).

| Mecánica | Activación (teclado) | Reglas clave |
|---|---|---|
| Caminar / correr | W A S D | No se mueve muerto ni durante ciertas animaciones de daño. |
| Sprint | **Mantener Shift** + dirección | ~+40 % de velocidad. Se cancela al recibir daño, bloquear o atacar. |
| Salto | Espacio | Solo desde el suelo. Se encadena con sprint y vault. |
| Vault (parkour) | **Espacio** cerca de un obstáculo compatible | Solo obstáculos de cierta altura/distancia; mantiene el impulso. |
| Esquivar | **Q** + dirección | ~0.5 s de desplazamiento, ~0.2 s invulnerable, cooldown 1 s. No durante ataque ni animación de daño. |
| Ataque ligero | **J** | Golpe (desarmado) o ataque rápido con arma. Hasta 3 encadenados, ~0.25 s entre ataques. |
| Ataque fuerte | **K** | Patada (desarmado) o golpe pesado con arma. 0.8 s, retroceso al enemigo, vulnerable si falla. |
| Bloqueo | **Mantener L** | Reduce ~70 % del daño, **solo ataques frontales**, reduce movilidad. |
| Combo | J → J → K | Se reinicia si pasan más de 0.5 s entre inputs. Con arma usa las animaciones del arma. |
| Armas | **E** para recoger | Katana, kanabo, yari. Una a la vez (recoger otra suelta la actual). No se rompen ni se mejoran. |
| Vida y daño | Automática | Nunca pasa del máximo ni baja de 0. **Regeneración** tras unos segundos sin daño. ~0.5 s de invulnerabilidad tras un golpe. |
| Caída mortal | Por contacto | Caer desde gran altura (barranco) = muerte instantánea. Es el único caso de muerte instantánea. |
| Checkpoints | Automática al cruzarlos | Guardan la posición de reaparición y el progreso del nivel. Una activación por checkpoint. |
| IA enemiga | Al entrar en su rango/zona | Acercarse, atacar, defenderse, esquivar, retroceder. **No sale de su zona.** Ataques cada ~0.5–1.5 s. |
| Boss fight | Al entrar en la zona del jefe | Bloquea la salida. Los jefes **no tienen fases**. |

### Valores de vida y daño (GDD §5.11)

| Personaje / ataque | Vida | Daño |
|---|---|---|
| Yukimura (jugador) | 100 HP (regenerable) | — |
| Golpe desarmado (J) / Patada desarmada (K) | — | 10 / 20 |
| Katana (J / K) | — | 20 / 35 |
| Yari (J / K) | — | 18 / 30 (mayor alcance) |
| Kanabo (J / K) | — | 30 / 50 (más lento) |
| Arquero | 50 HP | 10 por flecha |
| Guerrero ligero | 80 HP | 12 por corte |
| Guerrero pesado | 150 HP | 25 por golpe |
| Líder del clan rival (jefe Mundo 1) | 300 HP | 15–25 por ataque |
| El Comandante (jefe final) | 450 HP | 20–30 por ataque |

### Controles completos (GDD §14)

| Acción | Teclado | Gamepad (deseable) |
|---|---|---|
| Movimiento | W/A/S/D | Stick izquierdo |
| Sprint | Shift (mantener) + W/A/S/D | LT + stick izquierdo |
| Saltar / Vault | Espacio | A |
| Ataque ligero (golpe) | J | X |
| Ataque fuerte (patada) | K | Y |
| Bloquear | L (mantener) | LB |
| Esquivar | Q + dirección | RB |
| Recoger arma | E | B |
| Pausa | ESC | Start / Menu |

> Desde el 2026-09-30 el código usa estos controles (decisión P1), incluido el vault con
> **Espacio**, salvo **E** (recoger arma todavía no existe) y **ESC** (no hay pausa). Espacio actúa
> según el contexto: vault ante un obstáculo bajo, agarre ante una cornisa al alcance (fuera del GDD,
> P2) y, si no hay nada, salto. El slide, que está fuera del GDD, usa **C** corriendo con momentum (P27).
> Además, **S sin sprint camina hacia atrás sin girar** (P16). Gamepad: no. Ver `features.md` →
> "Diferencias GDD vs. implementación".

## 5. Enemigos y jefes (GDD §12, §13, §21)

| Enemigo | Tipo | Vida | Arma | Comportamiento | Debilidad | Puede soltar |
|---|---|---|---|---|---|---|
| Arquero | Básico | 50 | Arco | Mantiene distancia, busca posiciones elevadas | Combate cercano | — |
| Guerrero ligero | Básico | 80 | Katana | Se acerca rápido, ataques consecutivos, esquiva | Ataques fuertes; responder tras bloquear/esquivar | Katana |
| Guerrero pesado | Básico | 150 | Kanabo | Avanza lento, bloquea, embiste | Esquivar y atacar en su recuperación | Kanabo |
| Líder del clan rival | Jefe Mundo 1 | 300 | Katana | Presiona y aprovecha errores; combos, pesados, bloqueos, esquives, embestidas | Vulnerable tras sus ataques pesados | — |
| El Comandante | Jefe final | 450 | Katana | Adapta sus ataques al estilo del jugador | Pequeñas ventanas tras sus ataques | — |

**IA (GDD §21):** máquina de estados.
- Básicos: `Idle → Detectar jugador → Acercarse → Atacar → Defenderse → Buscar → Regresar`.
- Jefes: `Idle → Detectar → Perseguir → Analizar distancia → Atacar/Bloquear/Esquivar → Reposicionarse → Atacar nuevamente`.
- Los básicos son predecibles; los jefes usan más estados y ataques.

## 6. Mundo, niveles y progresión (GDD §7, §9, §10)

| Mundo | Ambientación | Niveles | Jefe |
|---|---|---|---|
| 1 — Los Campos de Japón | Campos de cultivo, bosques, aldeas destruidas, caminos de tierra, puestos militares | Nivel 1, Nivel 2 | Líder del clan rival (al final del Nivel 2) |
| 2 — La Capital en Guerra | Calles, barrios tradicionales, templos, murallas, tejados | Nivel 3 | El Comandante |

- 3 niveles en total, de ~15–20 min cada uno. Juego **lineal**.
- Checkpoints después de enfrentamientos o zonas importantes.
- Secretos: rutas alternativas de parkour donde puede haber armas.
- La dificultad sube con más enemigos por encuentro y obstáculos más complejos. Hay un único nivel
  de dificultad.
- **Progresión solo por historia:** no hay experiencia, árbol de habilidades, desbloqueables,
  mejoras, economía, monedas ni tiendas (GDD §7, §19).

## 7. Cámara, UI y flujo (GDD §15–§17)

**Cámara:** tercera persona sobre el hombro (Sleeping Dogs). Sigue suavemente desde atrás y un poco
a un lado; **el jugador puede mover la cámara libremente**. Distancia cercana, con ajuste ligero en
combate y parkour. Camera shake en golpes fuertes. En combate mantiene al jugador y al enemigo en
cuadro. En parkour deja ver el camino.

**UI minimalista:** sin barra de vida del jugador (la salud se comunica con el estado y las
animaciones del personaje, estilo Uncharted), sin barra de vida de enemigos ni jefes, sin indicador
de arma (se ve en las manos), diálogos como texto breve sin voz, y solo indicadores temporales cuando
hacen falta.

**Flujo de pantallas:**

```
Splash → Menú principal (Nueva partida · Continuar · Salir)
       → Gameplay (parkour · combate · checkpoints)
            ├─ Pausa (Continuar · Reiniciar desde checkpoint · Menú principal)
            ├─ Muerte → reaparecer en el último checkpoint con vida completa
            └─ Jefe / final de nivel → Siguiente nivel → … → Final del juego
```

**Guardado (GDD §26):** una única partida. Guarda el nivel alcanzado y el último checkpoint.
Autoguardado al completar un nivel y al activar un checkpoint.

**Victoria:** completar cada nivel; derrotar al líder del clan rival (fin del Mundo 1); derrotar al
Comandante (fin del juego y reencuentro con la familia).
**Derrota:** vida en 0 o caída desde gran altura → reaparecer en el último checkpoint.

## 8. Arte y audio (GDD §22–§23)

- **Arte:** realista y cinematográfico, Japón Sengoku. Madera, piedra y tejas. Paleta de tonos
  apagados (marrón, gris, verde oscuro, rojo, tierra). Iluminación natural: amaneceres, atardeceres,
  noches y antorchas.
- **Audio:** música tradicional japonesa que se intensifica en combate, con música propia para los
  jefes. SFX de pasos, saltos, golpes, bloqueos y armas. Ambiente de viento, lluvia, fuego y batalla
  a lo lejos. **Sin voces.**
- ⚠️ **Estado actual del arte:** el proyecto usa assets provisionales que **no** corresponden a la
  dirección de arte final. El GDD los permite (§28, riesgo "Dependencia de assets"):
  - Entorno: `LowPolyCity` (estilo cartoon "cyber").
  - Protagonista: personaje **Ch45** de Mixamo (`Assets/Characters/Player/character.fbx`), armadura
    oscura con sangre. No es el samurái "de apariencia sencilla" del GDD §22.
  - Animaciones: clips Humanoid retargeteados a Ch45: mocap del **Kinematica Demo** (locomoción por
    motion matching y vaults; Unity Companion License) y de **100STYLE** (retroceso y strafe; CC BY 4.0),
    **Quaternius** UAL (agacharse, slide, mantle, roll, jab, cross y reacciones al daño; CC0), mocap de la
    **CMU Mocap Database** (gancho y patada; uso libre con agradecimiento), **Dynamic Parkour System**
    (caída, aterrizajes y cornisa; MIT, animaciones de Mixamo), `LowPoly` (idle, salto, rolls de la
    esquiva, bloqueo) y dos transiciones de guardia de Ch45. Origen y licencia de cada una en el README de
    su carpeta en `Assets/ThirdParty/`; los créditos del juego deben nombrar 100STYLE y CMU.
    Las animaciones `LowPoly` vienen del paquete gratuito *FREE Low Poly Human - RPG Character* de
    la Unity Asset Store (lo indica el bloque `AssetOrigin` de sus `.meta`). Falta confirmar su
    licencia en la página del paquete antes de publicar el juego (arquitectura T15).

## 9. Alcance (GDD §25) y criterios de terminado (GDD §30)

**MVP (obligatorio):** movimiento en tercera persona · sprint y salto · vault · combate desarmado
(golpes y patadas) · ataque ligero y fuerte · un combo · bloqueo y esquive · vida con regeneración ·
personaje principal · 3 enemigos con IA básica · armas recogibles (katana, kanabo, yari) · 2 mundos
con 3 niveles · checkpoints · 2 jefes · menú principal · pausa · muerte y reaparición · victoria y
final · SFX y música básica · build para Windows.

**Deseable:** animaciones adicionales, mejoras visuales, gamepad, versión móvil.
**Extra:** cinemáticas, VFX avanzados.

**Riesgos que el GDD ya resolvió (§28), a respetar al implementar:**
- Parkour demasiado complejo → **limitarlo a salto, sprint y vault.**
- IA demasiado avanzada → máquinas de estados sencillas.
- Falta de experiencia con Unity → prototipar cada sistema por separado antes de integrarlo.
- Bugs de integración → integrar y probar constantemente.

**Planeación (GDD §27):**

| Semanas | Actividad |
|---|---|
| 1–2 | GDD, planificación, prototipo |
| 3–4 | Movimiento, cámara, físicas |
| 5–6 | Parkour y animaciones |
| 7–8 | Combate desarmado, combo, daño, armas |
| 9–10 | Enemigos, IA básica, jefes |
| 11–12 | 2 mundos y 3 niveles |
| 13 | Menú, pausa, checkpoints, muerte, guardado |
| 14 | Audio, efectos, ambientación |
| 15 | Pruebas, correcciones, optimización |
| 16 | Correcciones finales y build Windows |

**Testing (GDD §29):** probar después de cada mecánica integrada. Los bugs se registran como
`problema → causa → solución → estado`.

## 10. Lo que debes saber antes de modificar el proyecto

1. **El código viene de un diseño anterior.** El proyecto empezó como plataformero 2.5D con otro
   GDD. Lo que venía de ese diseño y no se usa ya se eliminó (XP, wall jump, enemigos
   *Looter/Brute*), y los comentarios que citaban el GDD anterior se corrigieron. Lo que sigue vivo
   de esa época (enemigos 2.5D, slide, ledge grab) está en `features.md` → "Diferencias GDD
   vs. implementación". Antes de usar un valor del código como referencia de diseño, compáralo con
   el GDD final.
2. **Decisiones de alcance** (P1–P10): están en `arquitectura.md` §8, con su estado. Resumen al
   2026-09-30: los controles del GDD **ya están implementados** (P1; el slide pasó a C); el wall jump
   se eliminó (ledge grab/climb y slide siguen activos); **no hay XP, estamina ni HUD** (GDD §7,
   §5.2, §16); los enemigos se rehacen en 3D con NavMeshAgent; la cámara se extiende con control de
   ratón; la iluminación horneada no se versiona; el personaje se anima con clips retargeteados
   (P11–P14). El parkour se basa en el Dynamic Parkour System adaptado a nuestra FSM (P15–P20): vault
   con Espacio, slide, auto step y cornisa. Desde el 2026-10-01 el parkour usa root motion solo
   mientras dura cada acción, warpeado con `MatchTarget` e IK de manos y pies sobre el contacto
   medido (P22). El movimiento base tiene escala humana: salto de ~1 m, inercia en el aire y
   aterrizaje según la altura (P23). `Level-1` es el Parkour Test Area (P24). Desde el 2026-10-02 hay
   un **Parkour Obstacle Standard** (P25): las medidas de los obstáculos y los límites de detección
   viven solo en `ParkourStandard`, y los obstáculos se construyen con los prefabs de
   `Assets/Prefabs/Parkour` (ver `arquitectura.md` §5.13). **Al construir un nivel, usa esos prefabs**
   en vez de cubos sueltos. No se usan active ragdoll ni plugins de animación (P26). La segunda pasada de
   movimiento (P27) dio momentum a la locomoción (la velocidad sigue al cuerpo, el torso se inclina),
   un slide contextual y transiciones de parkour sin cambios de velocidad (`arquitectura.md` §5.15–§5.16).
   **El movimiento y el parkour se están reconstruyendo** (P28, `arquitectura.md` §7.1): arquitectura
   híbrida propia, clips nuevos de Mixamo y alcance ampliado (caminar/agacharse, strafe y retroceso
   rápido, mantle, step over, drop y salto de cornisa). Fases 1–10 hechas (§7.1): cámara orbital,
   locomoción direccional con marchas, agacharse, slide con bucle real, mantle, drop y salto de
   cornisa, roll al aterrizar. Animaciones nuevas de Quaternius (CC0); los clips de Mixamo quedan para
   lo que falta (vault a dos manos, step over, agarre a la carrera, free hang, giros y frenadas). La migración a
   `.inputactions` sigue pendiente. **Desde el 2026-10-05 hay una segunda reconstrucción aprobada**
   (P29–P32, `arquitectura.md` §8): locomoción con motion matching (MxM) sobre el mocap del Kinematica
   Demo, `CharacterController` como único motor, Animation Rigging para el IK y repo privado por las
   licencias de Mixamo. Por ahora existen la fase 0 (el mocap importado y probado sobre Ch45 en
   `Assets/ThirdParty/Kinematica`), el retroceso y strafe de 100STYLE
   (`Assets/ThirdParty/100STYLE`, estilos Neutral y Rushed, hasta ~2 m/s, P34) y, desde el 2026-10-07, la
   fase 1: MxM embebido en `Packages/`, la base de datos horneada (`Assets/Data/MxM`) y su prueba en Play
   Mode (`arquitectura.md` §7.2). **Desde el 2026-10-08 (fase 2)** el jugador se mueve con un
   `CharacterController` (P30) y camina, corre y esprinta con motion matching a las velocidades del
   mocap (P33: 1.3 / 3.4 / 4.8 m/s, atrás ~2 m/s); el parkour y el combate siguen como acciones del
   Animator, reajustadas a esas velocidades. **Fase 3 (2026-10-08/09):** el vault es un clip de mocap
   elegido de un catálogo por obstáculo, velocidad y distancia, y warpeado sobre la geometría por código
   propio (P36: el vault alto bajó a 1.1 m y el medio estándar tiene 0.3 m de fondo); el combate
   desarmado sigue las fases medidas de sus clips, con la cadena jab → cross → gancho, la patada de mocap,
   objetivo, golpe por contacto, hit stop, reacción al daño y una esquiva más corta (P37), probado sobre un
   muñeco de entrenamiento en el área de pruebas. **Fase 3 del motion matching (2026-10-09):** un rig de
   **Animation Rigging** (P31) pone los pies sobre el terreno (bordillos y escalones incluidos), bloquea el
   pie de apoyo para que no patine y gira la cabeza hacia el objetivo, hacia donde se corre o hacia la
   cámara (`arquitectura.md` §5.10).
3. **No se implementa nada fuera del MVP** sin que antes funcione el MVP (GDD §25 y §28).

## 11. Cómo abrir el proyecto

- Usa **Unity 6000.6.0f1** exacto (Unity Hub). Otra versión puede provocar un reimport masivo y
  romper materiales o prefabs. La versión está en `ProjectSettings/ProjectVersion.txt`.
- Unity Hub → **Add** → selecciona la carpeta `warrior-woke/`. La primera importación tarda varios
  minutos.
- **Escena del juego:** `Assets/Scenes/Level-1.unity` (la única en Build Settings). Desde el
  2026-10-01 es el **Parkour Test Area** (P24): un suelo plano con perímetro y doce secciones hechas
  con los prefabs estándar (P25), sin textos (01 locomoción · 02 vault bajo · 03 vault medio ·
  04 vault alto · 05 slide · 06 cornisa · 07 muro de escalada · 08 salto y aterrizaje · 09 combinado ·
  10 laboratorio de fluidez · 11 mantle · 12 combate, con un muñeco de entrenamiento a la derecha de la
  entrada; ver `features.md` F32). Al dar Play apareces en su entrada,
  mirando hacia las secciones. Los niveles reales del GDD (§10) todavía no existen.
  `Assets/LowPolyCity/Scenes/CartoonLowPolyCityLite_01.unity` es solo la demo del asset pack y no
  tiene lógica del juego.
- **Auto-loader:** `Assets/scripts/Editor/SceneAutoLoader.cs` abre `Level-1.unity` al iniciar el
  Editor, **una vez por sesión** (`SessionState`), así que no interrumpe si luego abres otra escena
  a propósito.
- **Controles actuales** (GDD §14, P1, P28): ratón: mover la cámara (Escape libera el cursor, clic
  lo vuelve a bloquear) · WASD/flechas correr, relativo a la cámara · Ctrl (mantener) caminar, en
  cualquier dirección mirando a la cámara (strafe y hacia atrás) · S sin sprint correr hacia atrás
  sin girar (~2 m/s) · Shift (mantener) sprint (S con Shift: media vuelta) · Espacio, según el
  contexto: subirse a un bloque de 0.8–1.5 m (mantle), vault de un obstáculo de 0.45–1.1 m (si algún
  clip encaja con su altura, fondo, la velocidad y la distancia), agarrarse de una cornisa de 1.9–2.7 m o
  saltar (y agarrarse en el aire); corriendo se puede pulsar antes y la acción espera a su punto de
  inicio · C, según el contexto: deslizarse corriendo con momentum (pulsada antes de una barra, espera a
  alcanzarla; Espacio durante el slide encadena vault o salto), bajar a colgarse junto a un borde con
  caída, o agacharse / levantarse · colgado: Espacio sube, dirección contraria al muro suelta, Espacio +
  esa dirección salta lejos del muro · J ataque ligero (jab → cross → gancho; las pulsaciones durante un
  golpe quedan en cola) · K ataque fuerte (patada; remata el combo J → J → K) · los ataques giran y dan
  un paso hacia el objetivo más cercano de frente · L bloquear (mantener) · Q + dirección esquivar
  (después, J o K responden con un ataque). E (recoger arma) y ESC (pausa) todavía no hacen nada. Los rangos están en
  `ParkourStandard`.
- **Si cambias animaciones o el modelo:** corre **Tools → Warrior Woke → Configurar Animaciones del
  Jugador** (y **Validar Personaje** para revisar). Ver `arquitectura.md` §5.10.
- **Pruebas automáticas:** **Tools → Warrior Woke → Probar Personaje en Play Mode** recorre el
  Parkour Test Area con teclado simulado y reporta cada comprobación en la consola
  (`[PlayModeTest]`): el flujo de estados, el contacto físico medido sobre el esqueleto (manos en
  el borde, pies que no atraviesan nada, etc.), los vaults y el combate sobre el muñeco; deja
  storyboards en `Logs/PlayModeVaults` y `Logs/PlayModeCombat`. También en batch (ver `arquitectura.md` §5.9). Si
  cambias la escena, regenérala con **Tools → Warrior Woke → Construir Parkour Test Area**. Si
  cambias el estándar de obstáculos, regenera los prefabs con **Generar Prefabs de Obstáculos** y
  revisa con **Validar Obstáculos de Parkour**.
- **Motion matching** (la locomoción del jugador): **Tools → Warrior Woke → Construir Datos de
  Motion Matching** rehace la base de MxM (hazlo si cambias las tomas de la base) y **Probar Motion
  Matching** la prueba en Play Mode (`[MxMProbe]` en la consola, `Logs/MxMProbe/metrics.csv`). Ver
  `arquitectura.md` §7.2.
- **Vaults y combate:** **Construir Catálogo de Vaults** rehace `Assets/Data/Parkour/VaultCatalog.asset`
  (hazlo si cambias las tomas de vault; después corre **Configurar Animaciones del Jugador**) y **Revisar
  Clips de Combate** dibuja y mide los golpes sobre Ch45 (`Logs/CombatClips`), de donde salen los tiempos
  de `CombatTimings`. Ver `arquitectura.md` §5.17 y §5.4.

### "Hice pull y no veo los cambios"

1. Unity guarda qué escena tenías abierta en `Library/` (ignorada por git). Un pull que trae una
   escena nueva no cambia la escena abierta en tu Editor; el auto-loader lo resuelve al reabrir
   Unity.
2. Abriste la demo del asset pack en lugar de `Level-1.unity`.
3. Si ves objetos rosa/magenta o "missing script", Unity todavía está reimportando o estás usando
   otra versión de Unity.

## 12. Flujo de trabajo con git

- Haz `git pull` **antes** de abrir Unity y deja que termine de reimportar.
- **Nunca edites, borres ni muevas `.meta` fuera de Unity.** Mueve y renombra assets desde la
  ventana Project; si lo haces desde el Explorador de Windows se rompen las referencias (GUIDs).
- Si dos personas van a tocar **la misma escena o el mismo prefab**, avísense antes. Cuando se
  pueda, usen una rama por feature.
- En `git status` nunca debe aparecer `Library/`, `Temp/`, `Logs/`, `UserSettings/` ni `obj/`.
- **Commits:** Conventional Commits (`feat(scope): …`, `fix(scope): …`, `docs: …`, `chore: …`,
  `refactor: …`).
- **La iluminación horneada no se versiona** (decisión P9). `LightingData.asset`, los lightmaps y
  las reflection probes que Unity genera en `Assets/Scenes/<Escena>/` están en `.gitignore`: cada
  quien hornea en su máquina si lo necesita. La luz direccional de `Level-1` es **Mixed**, así que
  sin hornear la escena se ve con luz en tiempo real.
  - Al hornear, Unity también cambia la línea `m_LightingDataAsset` de la escena `.unity`. **No
    subas ese cambio** (descártalo con `git restore -p` o desde tu cliente de git).
  - No guardes otros assets dentro de subcarpetas de `Assets/Scenes/`: esas carpetas se ignoran.
  - `LightingData.asset` es binario aunque la serialización sea Force Text; `.gitattributes` lo
    marca como `binary` para que git no le convierta los finales de línea.

### Merge inteligente de Unity (una vez por máquina)

`.gitattributes` marca `.unity`, `.prefab`, `.mat`, etc. para `UnityYAMLMerge`. Cada máquina debe
registrar la herramienta una vez:

```
git config merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config merge.unityyamlmerge.driver "\"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Tools/UnityYAMLMerge.exe\" merge -p %O %A %B %A"
git config merge.unityyamlmerge.recursive binary
```

(macOS: `'/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Tools/UnityYAMLMerge'`.)

### `.gitignore`, `.gitattributes`, serialización y LFS

- `.gitignore` excluye `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, archivos de IDE,
  `ProfilerCaptures/`, `UIElementsSchema/` y la iluminación horneada (`Assets/Scenes/*/`).
- `.gitattributes` normaliza los finales de línea a LF, usa el merge de Unity para YAML y marca
  binarios (`.fbx`, imágenes, `.exr`, audio y `LightingData.asset`).
- Los `.meta` tienen `-diff`: `git diff` no muestra sus cambios. Si un GUID cambia, revísalo en el
  Editor o con `git diff --text`.
- La serialización es **Force Text** (`EditorSettings.asset`, `m_SerializationMode: 2`). No la
  cambies a Binary.
- **Git LFS solo para el mocap** (P35, 2026-10-05): `.gitattributes` manda a LFS los FBX de
  `Assets/ThirdParty/Kinematica/`, `Assets/ThirdParty/100STYLE/` y `Assets/ThirdParty/CMU/` y, desde el 2026-10-07, la base horneada
  de motion matching (`Assets/Data/MxM/*_AnimData.asset`, ~30 MB). Cada máquina necesita Git LFS (`git lfs install`, una vez); sin él,
  esos FBX llegan como punteros de texto y Unity no los importa. No reescribe el historial: el resto
  del arte sigue en git normal. Al final de `.gitattributes` hay un bloque comentado para extender LFS
  al resto de binarios; eso sí exigiría migrar el historial y coordinarlo con todo el equipo.

## 13. Equipo

| Integrante | Cuenta git | Aportes principales (según `git log`) |
|---|---|---|
| Alan Yahir Aguilar | `AlanYahirAguil` (20233tn135) | Setup inicial, URP, máquina de estados del jugador, parkour, combate, pooling/spawner, enemigos, XP (ya eliminado) |
| Axel Solano Castillo | `theisoluck` / antes `Axel Solano Castillo` (20233tn131) | Higiene de git, migración a 3D libre, cámara al hombro, pipeline de modelo del jugador, suelo/spawn, documentación, auditoría y limpieza contra el GDD |
| Angel | `AngelGUst` (20233tn103) | Estamina y HUD (retirados el 2026-09-30 por estar fuera del GDD, decisión P7) |

Si te unes al equipo, agrégate aquí con tu rol.
