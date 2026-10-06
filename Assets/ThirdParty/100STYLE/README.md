# 100STYLE — mocap de locomoción (estilo Neutral)

- **Autores:** Ian Mason, Sebastian Starke y Taku Komura (2022), dataset
  [100STYLE](https://doi.org/10.5281/zenodo.8127870).
- **Licencia:** **CC BY 4.0** (ver [`LICENSE.md`](LICENSE.md)). Permite usar, modificar y redistribuir,
  también comercialmente, **siempre con atribución**: los créditos del juego deben nombrar a los
  autores, el dataset y la licencia.
- **Para qué:** Kinematica no trae caminar o correr hacia atrás ni de lado; estas tomas lo cubren
  (decisión P34, 2026-10-05).
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
| `Character/Neutral_Skeleton.fbx` | esqueleto en pose de reposo (T-pose de los offsets del BVH) | — |

Las tomas originales son BVH a 60 fps en centímetros. `bvh2fbx.py` (Blender 4.5 LTS, en modo batch)
las recorta a los rangos de `Frame_Cuts.csv` (quita la T-pose y la preparación de los extremos), las
pasa a 30 fps como las de Kinematica, a metros, y las exporta a FBX:

```
blender -b --factory-startup -P bvh2fbx.py -- <carpeta con Neutral_*.bvh> <carpeta de salida>
```

(En Windows, Blender portátil falla al cargar numpy si su carpeta está en una ruta muy larga.)

## Importación en Unity

`Neutral_Skeleton.fbx` se importa como Humanoid con **mapeo explícito**, porque los nombres de
100STYLE engañan al mapeo automático: `Collar` es el hombro, `Shoulder` el brazo y `Hip` el muslo.
`Chest` = Spine, `Chest2` = Chest, `Chest4` = UpperChest (`Chest3` queda como hueso intermedio). Sin
dedos. Las tomas se importan como Humanoid copiando ese Avatar, con root motion completo. Lo hace
**Tools → Warrior Woke → Probar Retarget del Mocap** (`Assets/scripts/Editor/MocapRetargetProbe.cs`).

## Estado: 🔧 prueba de retarget

Todavía **no** las usa el jugador; entran a motion matching (MxM) junto con Kinematica.
