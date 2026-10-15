# NDMF Merge for ChilloutVR

NDMF Merge adds outfits, accessories, and armatures to a ChilloutVR avatar during an NDMF build. Configure sources on **CVR Merge Armature**, then use Manual Bake to inspect the merged avatar before uploading. Hierarchy and animation changes happen on the build clone; keep editing your original avatar and outfit assets.

Use it alongside [NDMF Avatar Optimization](https://github.com/MilchZocker/NDMF-Avatar-Optimization) to optimize the resulting avatar.

## Contents

- [Requirements and installation](#requirements-and-installation)
- [Quick start](#quick-start)
- [Features and workflow updates](#features-and-workflow-updates)
- [Armature Mode and bone matching](#armature-mode-and-bone-matching)
- [Object Mode and target-bone weighting](#object-mode-and-target-bone-weighting)
- [Configuration reference](#configuration-reference)
- [Blend shape tasks](#blend-shape-tasks)
- [Preview, presets, and logging](#preview-presets-and-logging)
- [Troubleshooting](#troubleshooting)
- [Build and release workflow](#build-and-release-workflow)

## Requirements and installation

| Dependency | Requirement |
| --- | --- |
| Unity | 2021.3 or newer, as declared in the manifest. Use a version supported by your CCK. |
| ChilloutVR CCK | Import into the Unity project before configuring your avatar. |
| [NDMF](https://github.com/bdunderscore/ndmf) | 1.4.1 or newer. |
| [Chillaxins — MilchZocker fork](https://github.com/MilchZocker/chillaxins) | Use this fork (currently 1.3.0) for CVR build integration. The manifest's VPM minimum of 1.1.0 does not select the fork. |
| Dynamic Bone / Magica Cloth | Optional, for outfits using those components. Magica rebuild support is compiled under `CCK_ADDIN_MAGICACLOTHSUPPORT`. |

Install CCK, NDMF, and the **[MilchZocker Chillaxins fork](https://github.com/MilchZocker/chillaxins)** first. Unity Package Manager does not resolve `vpmDependencies`; install this fork explicitly when using a Git URL or local package. It uses the same `dev.hai-vr.chillaxins` package ID as upstream, so replace an existing upstream installation rather than installing both copies.

### Install the Chillaxins fork

In Unity Package Manager, select **+ → Add package from git URL…** and use the fork's source package:

```text
https://github.com/MilchZocker/chillaxins.git?path=/Packages/dev.hai-vr.chillaxins#main
```

After the fork's UPM release workflow has been merged and has published its first `upm` branch, the release URL is:

```text
https://github.com/MilchZocker/chillaxins.git#upm
```

Alternatively, download the fork's package ZIP from [its releases](https://github.com/MilchZocker/chillaxins/releases), extract it, and use **Add package from disk…** to select the extracted `package.json`. Close and reopen Unity after installation so the bundled libraries load. Follow the fork's README for build hooks and Manual Bake options; upstream package-manager listings do not install this fork.

### Install NDMF Merge

**Unity Package Manager:** open **Window → Package Manager → + → Add package from git URL…** and enter:

```text
https://github.com/MilchZocker/NDMF-Merge.git#upm
```

The `upm` branch contains the published package at its root. To use the development source directly from `main`, select its package subdirectory:

```text
https://github.com/MilchZocker/NDMF-Merge.git?path=/packages/NDMF-Merge#main
```

**Manual installation:** download or clone this repository and copy **`packages/NDMF-Merge`** into your Unity project's `Packages/` directory. Include the package's `.meta` files and `package.json`; do not copy the entire repository as a package.

## Quick start

1. Put your base avatar in the scene with its **CVRAvatar** component and configured Animator/AAS base controller.
2. Add **NDMF Merge → CVR Merge Armature** to the same GameObject as **CVRAvatar**.
3. Add an entry in **Outfits to Merge** and assign **Outfit**. Sources can be prefab assets or scene objects. Keep scene sources outside the base avatar hierarchy to avoid including both the source and the merged copy.
4. Choose **Armature Mode** for skinned clothing or **Object Mode** for props and attachments. When assigning a source without a detectable armature, the inspector selects Object Mode automatically; check the selected mode.
5. For Armature Mode, set bone prefixes/suffixes to strip, click **Detect Mismatches**, and review the resolutions. For Object Mode, choose a placement mode and its target as described below.
6. If you need outfit animation layers, enable **Merge Animator (Basic)** or **Merge Animator (+AAS Layers)** on that entry. The global **Merge Animator** master switch must also be on. Per-outfit animator merging is off by default.
7. Keep **Merge Advanced Avatar Setup** and **Generate AAS Controller At End** enabled when merging AAS menus. Check the AAS base controller and combined sync budget before building.
8. Run NDMF **Manual Bake**. Inspect the hierarchy, test toggles and animations, and check deformation, culling, probes, and cloth. Edit the source avatar to adjust settings, then bake again.
9. Upload through the CVR/Chillaxins build integration once the result is ready. Play Mode processing depends on your installed NDMF/Chillaxins setup.

Outfit containers are temporary: their contents are merged or moved, then the containers are removed. Do not use the source container's enabled state as an outfit toggle. Configure the actual mesh/child objects and AAS animations for visibility, and verify them on the bake.

## Features and workflow updates

- **Two merge modes:** combine skinned armatures or place accessories using a parent, reference transform, explicit values, or a target bone.
- **Target-bone weighting:** attach existing skeletons while preserving world pose and skin weights. Convert rigid meshes to single-bone skins on the build clone.
- **Bone matching:** strip names, apply mappings, use optional semantic/fuzzy fallbacks, and choose a resolution for each mismatched bone.
- **Armature Merging settings:** select normalization scope, Scale Only or Apply Transform-Like baking, skeleton protection, and bindpose compensation.
- **Animator tools:** import layers and parameters, rewrite paths, optionally combine layers and masks, and align incoming Write Defaults with the detected base setting.
- **CVR integration:** merge AAS settings, parameter streams, and animator drivers; split drivers above 16 entries. Advanced Pointer Trigger merging remains a placeholder.
- **Safer AAS finalization:** run CCK Create + Attach once per target avatar, validate controller inputs and sync usage, isolate generated assets, and handle Manual Bake's asset import batching.
- **Blend shape tasks:** configure weight copying and frame generation, choose mapping methods and targets, and optionally extend existing animations with generated curves.
- **Clone-side repairs:** remap references to surviving bones and objects. Clips needing path or blend shape curve changes are copied and clone-side references are rebound.
- **Mesh utilities:** bounds/probe fixes, basic UV tools, material reuse, bone-chain checks, pre-merge validation, and post-merge verification.
- **Inspector workflow:** hierarchy comparison, statistics, presets, and root-level severity/verbosity controls.

## Armature Mode and bone matching

Use Armature Mode for outfits with skinned meshes and a bone hierarchy. The base avatar needs a detectable armature, and the outfit needs bones referenced by its skinned meshes.

For example, to match `Hoodie_Hips` to `Hips`, set **Prefix** to `Hoodie_`. Use **Mesh Prefix**, such as `Hoodie_`, to identify outfit mesh objects, and **Unique Bone Prefix** for bones that should remain distinct.

### Matching rules

Explicit conflict resolutions take priority. For automatic matching, the tool builds a name mapping table in this order: **Global Bone Name Mappings → Semantic Synonyms → Per-Outfit Bone Name Mappings**. Later entries override the same source key. Global mappings require **Enable Fuzzy Bone Matching**; semantic synonyms require semantic matching; per-outfit mappings apply independently.

After stripping the prefix/suffix, the tool applies the mapping table and looks for an exact target name. If no target is found, it can try Levenshtein matching, then semantic patterns and left/right variations. Levenshtein requires both **Enable Fuzzy Bone Matching** and **Enable Levenshtein Bone Matching**. Its default maximum distance is **1**; increase it carefully and review the matches. Semantic matching is off by default.

Prefer explicit mappings such as `pelvis → Hips` for known differences. Semantic patterns support `*` and `?`, but broad patterns can select unintended bones. Unique-prefixed bones are kept separate from automatic matching.

### Detect and resolve mismatches

Click **Detect Mismatches** after changing sources, transforms, or matching settings. Detection clears and rebuilds the conflict list, so review and reapply custom resolutions afterward. It compares matching bone transforms using these defaults:

| Setting | Default |
| --- | --- |
| Position Threshold | 0.001 m |
| Rotation Threshold | 0.5° |
| Detect Scale Conflicts | Enabled |
| Scale Threshold | 0.01, measured as vector difference magnitude |
| Default Bone Conflict Resolution | Force Merge (Snap) |

| Resolution | Result / use |
| --- | --- |
| **Force Merge (Snap)** | Map the outfit bone to the target avatar bone. Bindpose compensation helps preserve the skinned mesh's shape. |
| **As child, keep position and rotation** | Keep the outfit bone beneath the target with world pose preserved. Useful when direct merging distorts clothing. This reparents the bone. |
| **Rename (Keep Separate)** | Rename the source bone with `_Renamed` and keep it separate from the target. |
| **Don't Merge (Delete/Ignore)** | Skip mapping this bone to the target. Inspect the baked hierarchy and skinning before relying on it. |
| **Merge Into Selected…** | Map to **Custom Target Bone**, which must be assigned. |

## Object Mode and target-bone weighting

Object Mode bypasses armature matching and its per-outfit bone/mesh repair path. Use an empty source container with the accessory's mesh objects and any skeleton beneath it. The container is removed after processing, so put renderers and components that must survive on child objects.

The inspector permits Object Mode without a CVRAvatar, but the current build processor still requires **CVRAvatar on the merger GameObject**. Configure Object Mode on your base avatar.

| Positioning Mode | Behavior |
| --- | --- |
| **Place Under Selected GameObject** | Move the container's direct children beneath **Target GameObject**, preserving world transforms. An unassigned parent falls back to the avatar root. |
| **Place Using Reference** | Move direct children beneath the target parent and copy the resolved reference's world position/rotation and local scale. Assign **Reference Object**. |
| **Place Using Values** | Set each direct child's local position, Euler rotation, and scale beneath the parent. Default mode: zero position/rotation, unit scale. Values replace each child's local transform. |
| **Weight Meshes At Current Position To Target Bone** | Require a **Target Bone on the base avatar**. Attach existing bone hierarchy roots beneath it with world pose and skin weights preserved. Mesh and non-bone object branches stay under the avatar root. |

In weighting mode, unskinned meshes are weighted 100% to the target bone. A `MeshRenderer`/`MeshFilter` pair becomes a `SkinnedMeshRenderer` using a copied mesh and a bindpose for the current placement. Existing skinned meshes retain their weights; skinned meshes with no bones receive a rigid single-bone skin.

**Example: attach glasses to the head**

1. Create an empty `GlassesSource` container with the glasses mesh beneath it. Align the source for the intended avatar placement.
2. Add it to **Outfits to Merge**, select **Object Mode**, then **Weight Meshes At Current Position To Target Bone**.
3. Assign the base avatar's `Head` bone to **Target Bone**. It must be outside the source outfit.
4. Manual Bake and rotate the head on the baked avatar to check placement. Inspect the resulting renderer and bones.

Weighting requires separate mesh objects and bone transforms below the container. A renderer on the container or a bone transform, a skeleton using the container itself as a bone, or an object containing both renderer types causes validation to fail.

## Configuration reference

### Global defaults and per-outfit options

**Global Outfit Defaults** is an inspector bulk-apply tool. Changing it, or clicking **Apply to All Outfits Now**, copies values into the entries. Customize individual entries afterward; applying defaults again overwrites enum and boolean settings. Prefix/suffix and reference fields copy only when the global value is nonempty/non-null. Clear an entry directly when needed.

| Per-outfit setting | Purpose / default |
| --- | --- |
| **Outfit / Merge Mode** | Source container and Armature/Object behavior. |
| **Prefix / Suffix** | Strip text from bone names before matching. Empty. |
| **Unique Bone Prefix / Mesh Prefix** | Separate unmatched bones or label mesh objects. Empty. |
| **Bone Name Mappings** | Outfit-specific source-to-target name overrides. |
| **Bounds Fix Mode** | `None` (default), `CopyFromSelected`, or `RecalculateFromMesh`. Recalculate uses the shared mesh's bounds. |
| **Reference Body Mesh** | Body `SkinnedMeshRenderer` for copying bounds. Auto-detected if needed; assign explicitly if detection chooses the wrong mesh. |
| **Probe Anchor Sync Mode** | `AutoDetect` (default), `CopyFromSelected`, or `None`. Copies the reference's anchor and probe usage settings. |
| **Reference Probe Anchor Mesh** | Probe reference; may fall back to the bounds reference or auto-detection depending on mode. |
| **Force Scale To One** | Reset the cloned outfit root scale. Off; this differs from transform baking. |
| **Remove Unused Bones** | Optional cleanup after merging. Off. Inspect the result when other components reference bones. |
| **Merge Animator (Basic)** | Import the controller while skipping layers identified as AAS autogenerated layers. Off. |
| **Merge Animator (+AAS Layers)** | Include those generated layers too. Off. |

Bone matching, mesh prefixes, bounds/probe fixes, UV/material processing, and blend shape tasks belong to the Armature Mode processing path. Object Mode handles placement/weighting separately; these utilities do not all run on object entries.

### Armature Merging

| Setting | Purpose / default |
| --- | --- |
| **Enable Scale Normalization** | Normalize before merging. On. |
| **Normalization Scope** | `Outfits Only` (default) or `Base + Outfits`, on the build clone. |
| **Normalization Mode** | `Scale Only (Legacy)` (default) or `Apply Transform-Like`. |
| **Apply Scale To All Transforms** | Bake scale through bones and non-mesh objects. Off. |
| **Protect Skinned Skeleton** | Protect skeleton chains used by skinned meshes. On. |
| **Apply Position / Rotation To Static Meshes** | In Apply Transform-Like mode, bake selected transform components into static mesh data. Both on. |
| **Apply Position / Rotation To Skinned Meshes** | Bake into skinned mesh data with bindpose updates. Both on. |
| **Compensate Bindposes On Bone Remap** | Compensate source-to-target transform differences when remapping bones. On. |
| **Compensate Auto Mapped Bones** | Extend compensation to name/fuzzy mappings rather than only explicit conflicts. On. |

Start with defaults and inspect deformation after baking. Try Apply Transform-Like for transform-related issues; compare results before changing skeleton protection or normalizing the base.

### Animator and CVR settings

| Setting | Purpose / default |
| --- | --- |
| **Merge Animator** (master) | Allow controller imports enabled on outfit entries. On. |
| **Rewrite Animation Paths** | Rewrite clip bindings for the merged hierarchy, including motions inside blend trees. Affected clips are copied. On. |
| **Merge Avatar Masks** | Union masks for grouped incoming layers with the same original name. On. |
| **Combine Layers By Name** | Group incoming layers by original name. Off; separate layers normally receive outfit-name prefixes. |
| **Merge Advanced Avatar Setup** | Merge outfit AAS settings. On. |
| **Generate AAS Controller At End** | Use CCK Create + Attach after merging; requires AAS merging too. On. |
| **Advanced Settings Prefix** | Prefix merged AAS machine names. Empty. This is not a general namespace for all custom animator parameters. |
| **Merge Parameter Stream / Merge Animator Driver** | Process streams/drivers in the armature merge path. Both on. Drivers are split above 16 entries. |
| **Merge Advanced Pointer Trigger** | Placeholder: enabling it does not implement pointer-trigger merging. |
| **Merge Dynamic Bones / Merge Magica Cloth** | Enable component handling in the armature merge path. Both on. |

Write Defaults detection reads the default state of the base controller's first layer and applies its value to incoming states when available. Check mixed Write Defaults manually. Animator parameters are added by name; resolve incompatible same-name parameters before merging.

AAS entries with an already-present machine name are skipped. Use **Advanced Settings Prefix** when necessary and inspect the combined menu; the prefix also applies to copied display names.

**AAS prerequisites and generated assets:** configure a valid `AnimatorController` in the base avatar's AAS **baseController** field. If used, **baseOverrideController** must be an `AnimatorOverrideController`. Each AAS entry must have a setting implementation, and combined synced usage must fit the installed CCK's limit.

The plugin calls CCK Create + Attach on the build clone once per target avatar. It uses a unique build name to avoid overwriting source output, preserves source controller inputs, and verifies that saved animator/override assets were produced and attached. Build-only controller inputs are materialized under `Assets/NDMF Merge Generated` when necessary; CCK also writes its generated controller assets. Keep generated assets referenced by a baked avatar if you retain that bake.

### Mesh utilities, validation, and exclusions

| Group | Options and behavior |
| --- | --- |
| **UV Validation** | **Fill Missing UVs** is on and fills absent/invalid UV0 arrays with zeros. **Auto Fix Overlapping** and **Auto Fix Inverted** are off. Overlap detection uses a small degenerate-triangle heuristic and generates secondary UVs; inversion detection can flip UV0 vertically. These are basic repairs, not full texture unwrapping. |
| **Material Consolidation** | Enabled by default. Reuses material references within an outfit's skinned renderers using the same shader plus name similarity (threshold **0.9**) or a shared non-null `_MainTex`. It does not compare every shader property, combine meshes, or guarantee fewer draw calls. Review materials differing in color, keywords, or other textures. |
| **Bone Chain Validation** | Off; optionally warn about incomplete common spine/arm/leg chains. |
| **Pre-Merge Validation** | Missing-bone and mesh-integrity checks are on. |
| **Post-Merge Verification** | Bounds and probe checks are on. |
| **Exclusions** | Specify transforms or wildcard name patterns (`*`, `?`) to skip during armature processing. Exclusion does not guarantee a source object is copied into the final avatar; inspect the bake. |

## Blend shape tasks

Open **BlendShape Transfer** and click **+ Add Blend Shape Task**. Each task has independent **Enabled**, **Direction**, **Weight Transfer**, and **Frame Generation** settings. Use **Up**, **Down**, **Duplicate**, and **Delete** to manage the list. Legacy weight/generation settings migrate into tasks when the task list is empty.

Tasks run from the Armature Mode merge path, per processed outfit. Object-only setups do not run this pipeline. Review targets when using multiple outfits rather than assuming a task runs only once for the whole avatar.

### Copy current weights

Choose **Outfit → Base**, **Base → Outfit**, **Both Directions**, or **Mesh → Mesh**, then enable **Enable Weight Transfer**. For Mesh → Mesh, assign its **Source Mesh** and **Target Mesh**. **Match by Name** is on; disabling it uses index-based matching. **Min Weight Threshold** defaults to 0, and **Use Smart Weight Transfer** is on.

This copies current weight values. To create missing deformation frames, enable generation too.

### Generate frames and extend animations

1. Enable **Enable Generation** and assign its **Source Mesh**.
2. Select **Generation Targets**, or enable **Apply To All** for the base and all outfits. For **Mesh → Mesh**, assign the generation **Target Mesh** directly. Weight-transfer and generation mesh fields are separate.
3. Enter comma-separated **Blend Shapes** names, or leave empty to process all source shapes.
4. Choose **Transfer Method**: **Approximate (Nearest Vertex)**, **K-Nearest Blended**, or **Bounds Normalized Nearest**.
5. Start with **Max Mapping Distance = 0.01** and inspect results. Adjust for mesh scale/alignment. These methods approximate deformation across different topologies.
6. Leave **Override Existing** off to skip shapes already present on the destination. Enable it to replace existing shapes. **Use Smart Frame Generation** is on.
7. Enable **Add to Existing Animations** if existing base-avatar clips animate the source shape and should also animate the generated target shape. Affected clips are copied and rebound inside the clone.

For example, to generate a body fit shape on a hoodie, use the body as the generation source, select the hoodie target, enter the shape's name, and bake to inspect deformation. Weight copying alone cannot create that shape on the hoodie.

## Preview, presets, and logging

- **Hierarchy Comparison** displays proposed matches, match reasons, final names, and unique bones. It needs a CVRAvatar and detectable armature. Investigate matches here, then verify with Manual Bake.
- **Preview & Stats** summarizes meshes, materials, geometry, conflicts, components, and controllers. Refresh after changing sources/settings. Statistics are estimates, not build validation.
- **Save Current as Preset** stores a preset in the component's saved preset list. It captures the first outfit's bounds/probe modes, scale/cleanup flags, unique/mesh prefixes, plus global bone mappings. **Load** applies those outfit settings to all entries and replaces global mappings. This is a subset of configuration, not a full setup backup or standalone preset asset.
- **Logging** defaults to **Errors Only**. Use **Errors + Warnings** while configuring and **All Details** for investigation. **Verbose Logging** defaults to **Simple**; select **Detailed** with All Details for deeper diagnostics. Some build status/error messages are emitted independently of these filters.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Component/plugin does not load | Check Unity compile errors and installation of CCK, NDMF, and Chillaxins. Verify the package folder contains `package.json`, or use the correct Git URL above. |
| Nothing merges / no base avatar found | Put the merger on the CVRAvatar GameObject. Armature entries need a detectable base armature and outfit skinned bones. |
| Accessory disappears | Put renderers beneath an empty container. Choose Object Mode for rigid objects and assign its placement target. Inspect child active states on the bake. |
| Target-bone weighting fails | Select a base-avatar bone outside the outfit. Separate bone transforms and mesh objects below the source container. Check validation errors for unsupported layouts. |
| Clothes stretch | Review Detect Mismatches and mappings. Keep bindpose compensation on. Try **As child, keep position and rotation** for problematic bones and compare normalization modes. |
| Cloth explodes | Check component handling, normalization, and matching. Magica rebuild code requires the CCK Magica support define and compatible components. Test physics on a fresh bake. |
| Animations fail | Enable master and per-outfit animator options; check source controllers, Rewrite Animation Paths, parameter collisions, and Write Defaults. Test renamed mesh paths. |
| AAS generation fails | Read the CCK error. Check the AAS base controller, optional override type, valid entries, and combined synced bits. An incompatible CCK reflection API also produces an explicit error. |
| AAS toggles fail | Keep Merge Advanced Avatar Setup and Generate AAS Controller At End enabled. Inspect the bake's attached override controller and menu entries. |
| Blend shapes are absent | Enable the task and generation; assign generation source/target, check shape names/distance, and confirm an Armature Mode entry processes the task. Enable animation copying if needed. |
| Mesh vanishes at some angles | Try bounds copying from a suitable body renderer or recalculate from mesh. Verify bounds during animation. |
| UVs or materials look different | Review UV heuristics and material matching. Similar names or a shared texture do not imply all material properties match. Disable the relevant tool and compare a fresh bake. |
| References point to old bones/objects | Inspect remapping logs. Merged bones use the source-to-target map; other references may need unique names/paths to resolve. |
| Per-outfit settings changed unexpectedly | Global defaults and preset loading overwrite entry settings. Apply global values first, then customize outfits. |

## Build and release workflow

### NDMF processing

1. **Resolving — Merge Armatures:** clone outfit sources, normalize transforms, run the selected merge path, process AAS/driver data, remove temporary containers, remap references, and rebuild supported Magica data. Armature entries also run mesh utilities and blend shape tasks.
2. **Transforming — Merge Animators:** import enabled outfit layers/parameters and apply animation options.
3. **Transforming — Generate AAS Controller At End:** run CCK Create + Attach when enabled, remove merge components from the result, and save/refresh generated assets.

These are the plugin's registered passes. Use Manual Bake after configuration changes to inspect the combined result with other installed NDMF plugins.

### Repository layout

| Path | Purpose |
| --- | --- |
| [`packages/NDMF-Merge/Runtime/CVRMergeArmature.cs`](packages/NDMF-Merge/Runtime/CVRMergeArmature.cs) | Serialized settings, enums, migrations, and preset data. |
| [`packages/NDMF-Merge/Editor/CVRMergeArmatureEditor.cs`](packages/NDMF-Merge/Editor/CVRMergeArmatureEditor.cs) | Inspector, defaults, tasks, conflict tools, and previews. |
| [`packages/NDMF-Merge/Editor/CVRMergeArmaturePlugin.cs`](packages/NDMF-Merge/Editor/CVRMergeArmaturePlugin.cs) | NDMF passes, merging, controller generation, and remapping. |
| [`packages/NDMF-Merge/package.json`](packages/NDMF-Merge/package.json) | Package metadata and dependencies. |
| [`.github/workflows/upm-release.yml`](.github/workflows/upm-release.yml) | Publish the package branch when a GitHub release is published. |

The source links above describe the development repository. The generated UPM branch contains the package at its root instead.

### Publishing a package release

The **Update-UPM-Branch** workflow runs on GitHub **release → published**; pushing a tag alone does not trigger it.

1. Prepare changes on the intended release target branch and publish a GitHub release with a version tag such as `v1.2.1`.
2. The workflow checks out `release.target_commitish` with full history, strips a leading `v` from the tag, and updates the version in `packages/NDMF-Merge/package.json`.
3. If needed, the bot commits/pushes the manifest update back to the target branch. The workflow needs `contents: write` and permission to push there.
4. `hecomi/create-upm-branch-action` publishes from `packages/NDMF-Merge`, includes root `README.md` and `LICENSE.txt`, and receives the original release tag. Inspect the resulting `upm` branch/package and Actions run before directing users to update.

The repository currently has this release workflow rather than an automated Unity test workflow. Validate behavior in a Unity project with the CVR dependencies, including Manual Bake, AAS Create + Attach, target-bone weighting, and affected animation/physics behavior.

MIT License. See [LICENSE.txt](LICENSE.txt).
