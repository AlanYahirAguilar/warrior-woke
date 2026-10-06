100STYLE Dataset — Ian Mason, Sebastian Starke, Taku Komura (2022)

Licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0):
https://creativecommons.org/licenses/by/4.0/

Source: https://doi.org/10.5281/zenodo.8127870 (100STYLE.zip)
Paper: I. Mason, S. Starke, T. Komura. "Real-Time Style Modelling of Human Locomotion via
Feature-Wise Transformations and Local Motion Phases". Proceedings of the ACM on Computer Graphics
and Interactive Techniques 5(1), 2022.

Changes made for Warrior Woke: only the "Neutral" style is used; the BVH takes were trimmed to the
ranges listed in the dataset's Frame_Cuts.csv, resampled from 60 to 30 fps, scaled from centimetres
to metres and converted to FBX with Blender (see bvh2fbx.py in this folder). A rest-pose skeleton
(Character/Neutral_Skeleton.fbx) was exported from the same data.

The material is provided "as is", without warranties of any kind. The licensors do not endorse
Warrior Woke.
