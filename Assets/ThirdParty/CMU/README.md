# CMU — mocap del combate desarmado (patada y gancho)

- **Fuente:** CMU Graphics Lab Motion Capture Database (<http://mocap.cs.cmu.edu>), en la conversión a
  BVH "Daz-friendly, hip-corrected" de Bruce Hahne (cgspeed.com), tomada de
  <https://github.com/adventuring/mocap> (carpeta `CMU/`).
- **Licencia:** libre para investigación y uso comercial; se pide el agradecimiento "The data used in
  this project was obtained from mocap.cs.cmu.edu. The database was created with funding from NSF
  EIA-0196217." (ver [`LICENSE.md`](LICENSE.md)). Los créditos del juego deben incluirlo.
- **Para qué:** el ataque fuerte del GDD es una patada (§5.7) y la cadena ligera necesitaba un tercer
  golpe distinto del jab y el cross (decisión P37, 2026-10-08/09). Quaternius no tiene patada, su
  `Melee_Hook` es una embestida que deja el cuerpo en el aire y su `Hit_Knockback` una caída al suelo
  (revisados sobre Ch45 con **Tools → Warrior Woke → Revisar Clips de Combate**).
- **Versionado:** los FBX de esta carpeta van por **Git LFS** (P35, `.gitattributes`).

## Contenido

| Archivo | Toma original | Uso |
|---|---|---|
| `Animations/CMU_135_04_FrontKick.fbx` | sujeto 135 ("Martial Arts Walks"), toma 4 "Front Kick", completa | sub-clip `Kick_Front` (frames 187–221 a 30 fps, 6.25–7.35 s): peso atrás, rodilla arriba, patada frontal derecha y bajada a la guardia. Ataque fuerte (K) |
| `Animations/CMU_14_01_Hook.fbx` | sujeto 14 ("boxing"), toma 1, solo 21.2–22.25 s | sub-clip `Punch_Hook`: desde la guardia, paso adelante y gancho de derecha en arco horizontal a la altura de la cabeza, de vuelta a la guardia. Tercer golpe ligero (J3) |
| `Character/<toma>_Skeleton.fbx` | el esqueleto de cada sujeto en su pose de reposo (T-pose de los offsets del BVH) | Avatar Humanoid de sus tomas: cada sujeto de CMU tiene otras longitudes de hueso |

`bvh2fbx.py` (Blender 4.5, en modo batch) convierte una toma: centímetros a metros, 120 → 30 fps,
recorte al intervalo pedido (la primera frame es una T-pose) y los huesos `lButtock`/`rButtock`
plegados en el muslo (Humanoid toma el muslo como pierna superior y descartaba la animación del hueso
intermedio). Exporta la toma y el esqueleto en reposo:

```
blender -b --factory-startup -P bvh2fbx.py -- <toma.bvh> <carpeta de salida> <nombre> <inicio s> <fin s>
```

El gancho salió de buscar en las tomas de boxeo de CMU (13, 14, 15, 17, 79, 80, 141, 143, 144) los
golpes cuya mano barre en horizontal con el codo doblado a la altura del hombro; el de la toma 14_01
es el más limpio que empieza y termina en la guardia, con la misma postura (pie izquierdo adelante)
que el jab y el cross de Quaternius.

## Importación en Unity

`MocapRetargetProbe` (fuentes `CMU` = sujeto 135 y `CMU14` = sujeto 14) importa cada esqueleto como
Humanoid con el **mapeo explícito** de los nombres Daz (`Collar` es el hombro, `Shldr` el brazo,
`Thigh` la pierna; sin dedos de los pies) y cada toma copiando el Avatar de su sujeto. Con Foot IK el
retarget sobre Ch45 es limpio. `PlayerAnimationSetup` crea los sub-clips: rotación de la raíz y altura
horneadas en la pose, el paso del golpe como root motion (lo aplica el motor y lo escala el ataque para
no meterse en el objetivo) y un giro de la raíz que apunta el golpe al frente (el frente del clip es la
orientación del cuerpo al empezar, y la guardia de un boxeador está girada respecto al rival): −39° el
gancho y −8° la patada, medidos sobre Ch45.

## Medidas sobre Ch45 (CombatClipReview, a velocidad 1)

| Clip | Duración | Golpe | Alcance | Paso del cuerpo |
|---|---|---|---|---|
| `Kick_Front` | 1.13 s | el pie sube desde 0.30 y llega a máxima extensión a 0.48 (a la altura de la cadera) | 0.77 m desde la cadera | 0.37 m hasta el impacto (1.0 m en total) |
| `Punch_Hook` | 1.07 s | el puño barre de la derecha al centro entre 0.42 y 0.60, máxima extensión a 0.55 (altura de la cabeza) | 0.56 m | 0.38 m hasta el impacto |

## Estado: ✅ en uso

Los dos clips son estados del `PlayerAnimator.controller` (`HeavyAttack` y `LightAttack3`), con Foot IK.
Ver `docs/arquitectura.md` §5.4 y P37.
