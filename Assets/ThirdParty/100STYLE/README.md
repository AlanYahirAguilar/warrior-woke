# 100STYLE — mocap de locomoción (estilos Neutral y Rushed)

- **Autores:** Ian Mason, Sebastian Starke y Taku Komura (2022), dataset
  [100STYLE](https://doi.org/10.5281/zenodo.8127870).
- **Licencia:** **CC BY 4.0** (ver [`LICENSE.md`](LICENSE.md)). Permite usar, modificar y redistribuir,
  también comercialmente, **siempre con atribución**: los créditos del juego deben nombrar a los
  autores, el dataset y la licencia.
- **Para qué:** Kinematica no trae caminar o correr hacia atrás ni de lado; estas tomas lo cubren
  (decisión P34, 2026-10-05). **Neutral** (caminar normal) cubre las velocidades bajas y **Rushed**
  ("con prisa, balanceando los brazos") las altas. Ningún estilo pasa de ~2.2 m/s hacia atrás o de
  lado: el volumen de captura era pequeño. Se midieron Rushed, BigSteps, Elated, Followed, Proud,
  Strutting, Swat y ShieldedLeft/Right; Rushed fue el más rápido.
- **Versionado:** los FBX de esta carpeta van por **Git LFS** (P35, `.gitattributes`).

## Contenido

| Archivo | Toma original | Duración |
|---|---|---|
| `Animations/Neutral_FW.fbx` | caminar hacia adelante | ~119 s |
| `Animations/Neutral_FR.fbx` | correr hacia adelante | ~65 s |
| `Animations/Neutral_BW.fbx` | caminar hacia atrás | ~131 s |
| `Animations/Neutral_BR.fbx` | correr hacia atrás | ~86 s |
| `Animations/Neutral_SW.fbx` | caminar de lado | ~90 s |
| `Animations/Neutral_SR.fbx` | correr de lado | ~45 s |
| `Animations/Neutral_ID.fbx` | parado | ~15 s |
| `Animations/Neutral_TR1.fbx` | transiciones entre direcciones | ~108 s |
| `Animations/Rushed_{FW,FR,BW,BR,SW,SR,ID,TR1}.fbx` | las mismas 8 tomas en estilo Rushed | 8–108 s |
| `Character/Neutral_Skeleton.fbx` | esqueleto en pose de reposo (T-pose de los offsets del BVH) | — |

Las tomas originales son BVH a 60 fps en centímetros. `bvh2fbx.py` (Blender 4.5 LTS, en modo batch)
las recorta a los rangos de `Frame_Cuts.csv` (quita la T-pose y la preparación de los extremos), las
pasa a 30 fps como las de Kinematica, a metros, y las exporta a FBX:

```
blender -b --factory-startup -P bvh2fbx.py -- <carpeta 100STYLE> <carpeta de salida> Neutral Rushed
```

`<carpeta 100STYLE>` es la del ZIP (con `Frame_Cuts.csv` y una carpeta por estilo). Todos los estilos
usan el mismo esqueleto, así que todas las tomas copian el Avatar de `Neutral_Skeleton`.

(En Windows, Blender portátil falla al cargar numpy si su carpeta está en una ruta muy larga.)

## Importación en Unity

`Neutral_Skeleton.fbx` se importa como Humanoid con **mapeo explícito**, porque los nombres de
100STYLE engañan al mapeo automático: `Collar` es el hombro, `Shoulder` el brazo y `Hip` el muslo.
`Chest` = Spine, `Chest2` = Chest, `Chest4` = UpperChest (`Chest3` queda como hueso intermedio). Sin
dedos. Las tomas se importan como Humanoid copiando ese Avatar, con root motion completo. Lo hace
**Tools → Warrior Woke → Probar Retarget del Mocap** (`Assets/scripts/Editor/MocapRetargetProbe.cs`).

## Velocidades medidas (punta, m/s)

| Estilo | Adelante caminando / corriendo | Atrás caminando / corriendo | De lado caminando / corriendo |
|---|---|---|---|
| Neutral | 0.95 / 1.79 | 0.82 / 1.29 | 0.87 / 1.56 |
| Rushed | 2.20 / 2.87 | 1.60 / 2.04 | 1.46 / 2.21 |

Con Foot IK, Ch45 patina lo mismo que el esqueleto original (mediana 0.03–0.15 m/s) en los dos
estilos.

## Estado: 🔧 prueba de retarget

Todavía **no** las usa el jugador; entran a motion matching (MxM) junto con Kinematica.
