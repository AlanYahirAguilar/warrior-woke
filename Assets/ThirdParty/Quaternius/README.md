# Quaternius — Universal Animation Library 1 y 2 (versión Standard, gratuita)

- **Autor:** Quaternius ([quaternius.com](https://quaternius.com)).
- **Licencia:** **CC0 1.0** (dominio público), ver [`LICENSE.txt`](LICENSE.txt). Uso libre en proyectos
  personales, educativos y comerciales, sin atribución obligatoria (se da igual).
- **Origen de los archivos** (descargados el 2026-10-02, versión gratuita "Standard"):
  - UAL 1: <https://opengameart.org/content/universal-animation-library> →
    `universal_animation_librarystandard.zip` → `Unity/AnimationLibrary_Unity_Standard.fbx`
    (aquí `Animations/UAL1_Standard.fbx`).
  - UAL 2: <https://opengameart.org/content/universal-animation-library-2> →
    `universal_animation_library_2standard.zip` → `Unity/UAL2_Standard.fbx`
    (aquí `Animations/UAL2_Standard.fbx`).
  - Los zip completos quedan fuera del repo, en `Recursos/Quaternius/`.

## Qué se usa

`Tools → Warrior Woke → Configurar Animaciones del Jugador` importa los dos FBX como Humanoid con su
propio Avatar y **solo** estas tomas (las demás no se importan). En el archivo su rig mira hacia −Z,
así que, como los clips LowPoly y del DPS, la raíz conserva su orientación original ("Original") y
lleva un offset de 180°: el clip mira y avanza hacia delante. Con la raíz basada en la orientación
del cuerpo el resultado dependía de la pose y salía de espaldas (lo detectó la prueba "la pose mira
hacia donde mira el cuerpo"). `Medir Clips` mide cada clip en el marco de su raíz tal como lo juega
el Animator.

| Toma | Clip | Uso en el proyecto |
|---|---|---|
| `Rig|Crouch_Idle_Loop` (UAL1) | Crouch_Idle | Agachado quieto (estado `Crouch`) |
| `Rig|Crouch_Fwd_Loop` (UAL1) | Crouch_Fwd | Agachado caminando |
| `Rig|Roll` (UAL1) | Roll | Aterrizaje fuerte a la carrera absorbido con un roll (`LandRoll`) |
| `Armature|Slide_Start` (UAL2) | Slide_Start | Entrada al slide (la cadera llega al suelo a los 0.35 s) |
| `Armature|Slide_Loop` (UAL2) | Slide_Loop | Slide: **bucle real** (su primer y último frame coinciden) |
| `Armature|Slide_Exit` (UAL2) | Slide_Exit | Salida del slide a la carrera |
| `Armature|ClimbUp_1m_RM` (UAL2) | ClimbUp_1m | Mantle: subirse a un bloque de 0.8–1.5 m (root motion, sube 1.02 m y avanza 1.72 m) |

Reemplazan al slide del Dynamic Parkour System, cuyo tramo central se usaba como bucle sin serlo y
hacía "reiniciarse" el slide. Detalle técnico: `docs/arquitectura.md` §5.10 y §7.1.
