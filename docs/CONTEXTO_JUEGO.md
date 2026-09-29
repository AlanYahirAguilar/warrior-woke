# Contexto del juego — Warrior Woke

> **Nota honesta:** no hay un GDD (Game Design Document) dentro de este repositorio. Todo lo de
> este documento está **inferido del código, los assets y los comentarios** (varios scripts citan
> secciones de un GDD externo — `GDD §8`, `GDD §14`, etc. — que el equipo debe tener en algún otro
> lado, p. ej. Google Docs). Si existe ese documento, agrégalo al repo (por ejemplo en
> `docs/GDD.md` o un link en este archivo) y actualiza esta sección con la fuente real en vez de
> la inferencia.

## Qué es, hasta donde el código lo dice

**Warrior Woke** es un juego de acción con combate cuerpo a cuerpo. Nació como un
**plataformero 2.5D** (movimiento en un solo eje, cámara lateral, parkour tipo
Prince-of-Persia/Ori: vault, colgarse de cornisas, wall-jump) y durante esta sesión se migró a un
esquema de **acción en tercera persona con cámara al hombro** (estilo Sleeping Dogs/GTA),
manteniendo el combate y descartando temporalmente el parkour (ver `docs/CAMBIOS.md` y
`docs/ARQUITECTURA.md` para el porqué técnico).

## Ambientación

Ciudad de baja poligonización (`Assets/LowPolyCity/` — pack "Cartoon Low Poly City") — casas
estilo "cyber" (`House_01_cyber`), calles, iluminación horneada. `Level-1` es, por ahora, un
espacio pequeño con un par de edificios, no una ciudad abierta completa — es la escena de
prueba/bloqueo donde se está iterando el controlador y el combate antes de construir el nivel
real.

## Personaje jugable

Un solo protagonista humanoide (modelo importado en `Assets/Characters/Player/`), controlado en
tercera persona. No hay indicios en el código de una identidad/historia particular para el
personaje (nombre, rol, motivación) — el "Woke" del título y el nombre de archivo
`Protagonista.fbx` son las únicas pistas, y no alcanzan para inferir una premisa narrativa. Si el
equipo tiene una historia/lore definida, es información que vive fuera del código y vale la pena
documentar aquí.

## Mecánicas confirmadas por el código

### Movimiento
- Correr, saltar, deslizarse (slide), auto-sprint tras mantener una dirección unos segundos.
- **Antes de esta sesión**: un solo eje (izquierda/derecha), con vault (saltar obstáculos bajos),
  colgarse de cornisas (ledge grab/climb) y wall-jump (hasta 2 consecutivos antes de tocar el
  suelo).
- **Desde esta sesión**: movimiento libre en 3D relativo a cámara (adelante/atrás/lados). El
  parkour queda pausado — ver `docs/ARQUITECTURA.md`, sección "Deuda técnica conocida".

### Combate (referencias explícitas a un GDD externo en el código)
- **Ataque ligero** (click izquierdo): ~0.25s, encadenable hasta 3 veces seguidas.
- **Ataque pesado** (click derecho): ~0.8s, más lento, sin ventana de cancelación — falla y quedas
  expuesto.
- Un ataque ligero puede **cerrar el combo con un ataque pesado** (finisher) dentro de una ventana
  de 0.5s.
- **Bloqueo** (F, mantenido): reduce el daño recibido drásticamente (~95% según el código actual —
  el comentario dice "~70% per GDD", puede que el valor se haya ajustado después sin actualizar el
  comentario; vale la pena confirmar cuál es el vigente).
- **Esquiva** (E): dash corto hacia donde mira el personaje, con vulnerabilidad reducida
  (i-frames ~0.2s de los 0.5s que dura), cooldown de 1s. Solo en el suelo.
- Vida con clamp 0–100 por defecto, con i-frames tras recibir daño (evita "combos infinitos" de
  golpes pegados).

### Enemigos
`Enemy.prefab` por ahora solo tiene `HealthSystem` + `Hitbox` — es un objetivo que puede recibir y
hacer daño, **sin IA propia todavía** (no hay un script de comportamiento/patrulla/persecución en
el repo). El combate contra enemigos reales (decisiones, patrones de ataque) es trabajo pendiente.

## Estado actual vs. visión a futuro

| Área | Estado actual | Pendiente/Futuro |
|---|---|---|
| Movimiento | Libre en 3D, cámara al hombro | — |
| Cámara | Al hombro, con evasión de colisión | Posible mouse-look independiente si se quiere más control de encuadre |
| Combate | Ligero/pesado/bloqueo/esquiva funcionando | Animaciones (hay TODOs en el código), IA enemiga |
| Parkour | Código completo pero deshabilitado | Redecidir si vale la pena adaptarlo a 3D libre, o si el diseño ya no lo necesita |
| Nivel | Bloqueo pequeño (2 casas + suelo agregado esta sesión) | Nivel real acorde al GDD |
| Narrativa | Sin evidencia en el código | Documentar si existe en otro lado |

## Cómo mantener este documento útil

Actualízalo cuando una decisión de diseño quede confirmada (no inferida) — por ejemplo, cuando se
decida si el parkour vuelve o no, cuándo se defina la premisa narrativa, o cuando haya IA de
enemigos. Si encuentras el GDD real del equipo, esa es la fuente de verdad por encima de este
documento — reemplaza la inferencia por la cita real.
