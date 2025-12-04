# NDMF Merge for ChilloutVR

NDMF Merge is a non-destructive armature and outfit merger for ChilloutVR avatars. It clones your base avatar during the NDMF bake, grafts selected outfits onto that clone, remaps references, and leaves the originals untouched. Use it to keep prefabs clean while rapidly iterating on outfits, Advanced Avatar Settings (AAS), and animator setups.

> **Complementary tool:** NDMF-Avatar-Optimizer is tuned to work alongside NDMF Merge. Run optimization after merging to keep the baked avatar lean.

## Features at a glance
- Add a **CVR Merge Armature** component to your avatar and drive the entire merge from one inspector.
- Merge multiple outfits or accessory armatures at build-time without modifying source prefabs.
- Per-bone conflict detection with configurable resolutions (keep, rename, or skip).
- Optional merging of CVR-specific systems (AAS, Parameter Stream, Animator Driver) and DynamicBone/Magica Cloth components.
- Animator merging per outfit plus a master toggle, including a mode that preserves AAS auto-generated layers.
- Automatic remapping of external references in the cloned bake and post-merge Magica Cloth data rebuild.

## Requirements
- Unity 2021.3+
- ChilloutVR CCK installed in the project
- [NDMF 1.4.1](https://github.com/bdunderscore/ndmf) (dependency)
- [Chillaxins 1.1.0+](https://docs.hai-vr.dev/docs/products/chillaxins) (VPM dependency)

## Installation
1. Add the package via VPM (recommended) or by copying the `packages/NDMF-Merge` folder into your Unity project.
2. Ensure NDMF is present in the project and the CCK assemblies are available so reflection-based CVR lookups can succeed.
3. (Optional) Install NDMF-Avatar-Optimizer if you want an optimized bake after merging.

## Typical workflow
1. **Add component:** Add **CVR Merge Armature** to your avatar GameObject that already has a `CVRAvatar` component.
2. **Configure outfits:** In **Outfits to Merge**, add each outfit or armature prefab you want to combine. Optionally set prefixes/suffixes and animator options per outfit.
3. **Detect bone conflicts:** Press **Detect Conflicts in All Outfits** to scan bones actually used by meshes and review the per-bone resolutions. Adjust resolutions or clear entries as needed.
4. **Tune settings:** Use **Advanced Settings** to exclude transforms, pick component merging options, and decide which CVR features/animators should be merged.
5. **Bake trigger:** The merge runs when NDMF processes the avatar (Play Mode, Bundle/Upload, or **Manual Bake**). NDMF clones the avatar, applies the merge to the clone, and destroys the temporary merger component in the output.
6. **Review & publish:** Inspect the baked clone, verify Magica/DynamicBone data, then proceed with upload or further optimization.

## Component properties
### Outfits to Merge
Each entry represents one outfit or accessory armature to be merged. All options are per-outfit:
- **Outfit:** GameObject root of the outfit/armature to merge.
- **Prefix / Suffix:** Strings to strip from bone names while matching them to the target armature.
- **Unique Bone Prefix:** Applied to bones that do not exist on the target armature so they remain namespaced.
- **Mesh Prefix:** Prepended to all mesh GameObject names for clarity after merge.
- **Merge Animator (skip AAS auto-layers):** Merge this outfit’s AnimatorController, omitting AAS auto-generated layers so only authored layers come across.
- **Merge Animator (include AAS auto-layers):** Merge the AnimatorController including AAS auto-generated layers for one-to-one parity with the outfit prefab.

### Bone Conflict Resolution
- **Default Bone Conflict Resolution:** Fallback choice for newly detected conflicts (Still Merge, Rename, Don’t Merge).
- **Conflict Threshold:** World-space transform difference threshold used to decide when two bones are considered conflicting.
- **Bone Conflicts list:** Populated by **Detect Conflicts in All Outfits**; shows per-bone position/rotation/scale deltas with individual resolution pickers and bulk “All: …” buttons.

### Exclusions
- **Excluded Transforms:** Specific transforms that are left untouched during merge operations.
- **Excluded Name Patterns:** Wildcard patterns (`*` and `?`) that skip matching transforms whose names fit the pattern.

### Component Merging Options
- **Lock Parent Scale:** Force parent scale to 1 to prevent outfit scaling artifacts.
- **Merge DynamicBones:** Copy DynamicBone components from outfits.
- **Merge Magica Cloth:** Copy Magica Cloth components from outfits and trigger a data rebuild after merging.

### CVR Component Merging
- **Merge Advanced Avatar Settings:** Merge AAS entries from outfit avatars into the target avatar. When enabled:
  - **Generate AAS Controller At End:** Runs CVR “Create Controller” after all merges to regenerate the animator with merged entries.
  - **Advanced Settings Prefix:** Optional prefix applied to merged AAS entries to avoid naming conflicts.
- **Merge Advanced Pointer/Trigger:** Copy CVR advanced pointer/trigger components.
- **Merge Parameter Stream:** Combine Parameter Stream entries across avatars.
- **Merge Animator Driver:** Merge Animator Drivers with automatic split handling to keep within parameter limits.

### Animator Merging (Master)
- **Merge Animator:** Global guard. If disabled, no outfit animators merge regardless of per-outfit toggles.

### Broken Animator References (debugging)
A readonly list showing any detected animator references that could not be automatically remapped.

## What happens during the bake
When NDMF runs, the plugin performs these steps:
1. **Discover mergers:** Finds all `CVRMergeArmature` components under the avatar root.
2. **Clone outfits:** Each configured outfit is instantiated under the avatar clone so originals stay untouched.
3. **Merge bones and meshes:** Bones are matched using the configured prefixes/suffixes; excluded transforms are skipped; conflicts respect your chosen resolutions.
4. **Merge components:** DynamicBone, Magica Cloth (if enabled), CVR systems (AAS, Parameter Stream, Animator Driver, advanced pointers/triggers) and per-outfit animator controllers are merged according to toggles. A master animator toggle gates all per-outfit animator merges.
5. **Animator handling:** Animator merges occur in the Transforming phase so merged controllers can be used for later steps. If AAS merge is enabled, the optional **Generate AAS Controller At End** pass rebuilds the final controller after all merges.
6. **Reference remap:** A universal sweep walks all serialized and reflected object references in the clone, remapping any links that pointed outside the avatar to their new equivalents (or nulling when no match exists).
7. **Magica rebuild:** After remapping, Magica Cloth components are rebuilt via their `BuildManager` to regenerate internal data.
8. **Cleanup:** Temporary merged outfit clones are destroyed and the `CVRMergeArmature` component is removed from the baked output.

## Magica Cloth 1 interference
Mesh edits and bone modifications can disrupt Magica Cloth 1 or other components that rely on original mesh data. The plugin attempts to rebuild Magica data automatically, but the detection/guard logic is not fully reliable. If cloth behaves incorrectly, manually rebuild cloth mesh data after processing. For Magica Cloth 1 setups, prefer **Manual Bake** instead of automatic Play/Bundle triggers and review the results before publishing.

## Using with NDMF-Avatar-Optimizer
NDMF-Avatar-Optimizer is recommended after merging to slim the baked avatar. Run it on the merged clone produced by NDMF Merge so it can clean up the final hierarchy and assets.

## Tips for reliable results
- Keep outfit prefabs untouched; re-run NDMF Merge after changes instead of editing the baked output.
- Use name prefixes on unique bones/meshes to avoid ambiguity when multiple outfits add similar hierarchies.
- Detect and resolve bone conflicts early, especially when outfits include their own armature edits.
- When merging animators, decide whether AAS auto-layers should be kept or skipped per outfit.
- If you rely on external scene references, ensure they exist under the avatar before baking so the remapper can resolve them.

## License
MIT. See [LICENSE.txt](LICENSE.txt).
