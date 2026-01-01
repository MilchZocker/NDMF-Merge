# NDMF Merge for ChilloutVR

**NDMF Merge** is a powerful, non-destructive tool for ChilloutVR (CVR) that merges outfits, accessories, and armatures onto your base avatar at build time.

It allows you to keep your project clean by keeping outfits as separate prefabs. When you upload or enter Play Mode, the tool clones your avatar, grafts the outfits, handles bone merging, rebuilds physics, and generates the necessary controllers—all without touching your source files.

> **Recommended:** Use this alongside [NDMF-Avatar-Optimizer](https://github.com/MilchZocker/NDMF-Avatar-Optimization). NDMF Merge combines the avatars, and Avatar Optimizer cleans up the resulting hierarchy.

## ✨ Key Features

*   **Non-Destructive Workflow:** Your source assets remain untouched. All merging happens on a temporary clone during the build process.
*   **Universal Reference Remapper (v2):** Uses a deep reflection-based sweep to find scripts referencing the *old* outfit objects and automatically points them to the *new* merged avatar. This fixes broken scripts and missing references on custom components.
*   **Smart Bone Merging:**
    *   **Merge:** Snaps outfit bones to the avatar's armature.
    *   **Constraint Mode:** Can resolve bone conflicts by creating a **ParentConstraint** with zero offset. Great for outfits that don't perfectly align with the base armature but need to follow it.
    *   **Rename/Unique:** Keeps bones separate if they shouldn't merge.
    *   **Semantic Bone Matching:** Use synonyms and patterns to match bones with different naming conventions (e.g., 'pelvis' → 'Hips').
    *   **Levenshtein Fuzzy Matching:** Automatically match bones with similar names using edit distance algorithms.
*   **Intelligent Animator Merging:**
    *   Merges Animator Controllers from outfits.
    *   **Auto-detects Write Defaults:** Scans your base avatar's controller to enforce consistent Write Defaults settings on merged layers.
    *   **Animation Path Rewriting:** Automatically rewrites animation clip paths to match the merged hierarchy.
    *   **Avatar Mask Merging:** Combines avatar masks from multiple layers with the same name.
    *   Splits **Animator Drivers** automatically if they exceed the 16-parameter limit.
*   **Full CVR Component Support:**
    *   Merges **Advanced Avatar Settings (AAS)**, creating a unified menu.
    *   Merges **Parameter Streams** and **Pointer/Trigger** components.
    *   Regenerates the AAS Animator Controller at the end of the build to ensure all animations allow for toggling.
*   **Physics Support:**
    *   Supports Dynamic Bones.
    *   **Magica Cloth Integration:** Automatically triggers a data rebuild for `MagicaRenderDeformer`, `VirtualDeformer`, `BoneCloth`, and `MeshCloth` after merging to prevent "exploding" mesh issues.
*   **Advanced Mesh & Material Tools:**
    *   **UV Validation:** Automatically fill missing UVs, fix overlapping UVs, and correct inverted UV winding.
    *   **Material Consolidation:** Merge duplicate materials by shader and texture to reduce draw calls.
    *   **Smart Material Matching:** Match materials by name similarity and shader type.
*   **Blend Shape Tools:**
    *   **Weight Transfer:** Copy blend shape weights between base and outfit meshes.
    *   **Frame Generation:** Generate blend shapes on outfit meshes from source meshes with topology matching.
    *   **Smart Generation:** Use topology-aware algorithms for better blend shape quality.
*   **Validation & Quality Control:**
    *   **Pre-Merge Validation:** Check for missing bones and invalid meshes before merging.
    *   **Post-Merge Verification:** Verify bounds and probe anchor settings after merge.
    *   **Bone Chain Validation:** Validate common bone chains (spine, legs, arms) for completeness.
*   **Comprehensive Logging:**
    *   **Global Verbose Logging:** Master control with 3-level log detail (Errors Only, Warnings+Errors, All Details).
    *   **Per-Category Logging:** Independent verbose logging for each tool category (UV, Materials, Blend Shapes, etc.).

---

## 📦 Requirements

*   Unity 2021.3+
*   **ChilloutVR CCK** (Imported in the project)
*   **[NDMF](https://github.com/bdunderscore/ndmf) 1.4.1+**
*   **[Chillaxins](https://docs.hai-vr.dev/docs/products/chillaxins) 1.1.0+** (Required for the plugin to load via VPM)
*   *(Optional)* **Magica Cloth** (If merging cloth components)

---

## 🚀 Setup Guide

### 1. Installation

**Option A: Via Unity Package Manager (Git URL)**
1. Open Unity Package Manager (Window → Package Manager)
2. Click the **+** button in the top-left corner
3. Select **Add package from git URL...**
4. Enter: `https://github.com/MilchZocker/NDMF-Merge.git#upm`
5. Click **Add**

**Option B: Manual Installation**
Copy the `NDMF-Merge` folder into your project's `Packages/` directory.

### 2. Prepare the Scene
1.  Place your **Base Avatar** in the scene.
2.  Place your **Outfit Prefabs** in the scene keeping them separate ensures they don't get uploaded twice if you forget to remove them.
3.  **Disable the Outfit GameObjects** if you don't want them visible by default, OR leave them enabled if you want them to be part of the base mesh. *NDMF Merge will handle the instantiation.*

### 3. Add the Component
Select your **Base Avatar** (the object with the `CVRAvatar` component).
Click **Add Component** -> search for **CVR Merge Armature**.

### 4. Configure Outfits
In the `CVR Merge Armature` inspector:
1.  Click **+** under the **Outfits to Merge** list.
2.  Drag your Outfit Prefab into the **Outfit** slot.
3.  *(Optional)* Set the **Mesh Prefix** (e.g., `Hoodie_`) to easily identify meshes in the final build.
4.  *(Important)* Configure **Prefix/Suffix** stripping (see below).

### 5. Detect and Resolve Conflicts
1.  Click the **🔍 Detect Mismatches** button in the Bone Conflicts section.
2.  The tool will scan for bones that exist in both the Base Avatar and the Outfit.
3.  Scroll down to **Bone Conflicts**. You will see a list of matching bones with position/rotation/scale differences.
4.  **Review Resolutions:**
    *   **Force Merge (Snap) - Default:** The outfit bone is deleted, and its children/weights are moved to the Base Avatar's bone. Use for exact matches.
    *   **Constraint To Target (Safe):** The outfit bone is kept, moved to the root, and **Parent Constrained** to the base bone. Use this if the merge causes deformation because the bones aren't in the exact same spot.
    *   **Rename (Keep Separate):** Keeps the bone entirely separate with a unique name. Use for bones that shouldn't merge.
    *   **Don't Merge (Delete/Ignore):** Skips merging this bone entirely.
    *   **Merge Into Selected...:** Specify a custom target bone for this merge.

### 6. Configure Advanced Settings (Optional)
Expand the various sections to customize:
*   **🌐 Global Outfit Defaults:** Set default values for all outfits
*   **🔗 Global Bone Matching:** Enable fuzzy matching, Levenshtein distance, add global bone mappings
*   **🎭 Semantic Bone Matching:** Add synonyms and patterns for different bone naming conventions
*   **🎬 Animator Improvements:** Enable animation path rewriting, avatar mask merging
*   **🔍 Bone Conflict Resolution:** Configure tolerance thresholds and default resolution behavior
*   **🧵 Mesh & UV Tools:** Configure UV validation and material consolidation
*   **🙂 Blend Shape Transfer:** Set up weight transfer or blend shape generation
*   **⛓ Bone Chain Validation:** Enable validation of common bone chains
*   **✅ Validation (Pre/Post):** Configure pre-merge and post-merge validation checks
*   **⚙ Advanced Settings:** Configure component merging and CVR-specific features
*   **🚫 Exclusions:** Exclude specific objects or name patterns from merge
*   **🐛 Debug & Logging:** Set log level and enable per-category verbose logging
*   **📊 Preview & Analysis:** View hierarchy comparison and merge statistics
*   **🐛 Debug & Logging:** Set log level and enable per-category verbose logging

### 7. Bake
Simply upload your avatar or use **Manual Bake** in NDMF settings. NDMF runs automatically during the build process.

**Recommended:** Use **Manual Bake** first to inspect the result before upload, especially for Magica Cloth setups.

---

## ⚙️ Detailed Configuration

### Per-Outfit Settings

For each entry in the "Outfits to Merge" list:

#### Basic Settings
| Setting | Description |
| :--- | :--- |
| **Outfit** | The GameObject (prefab) of the clothes/props you want to add. |
| **Prefix / Suffix** | Text to *remove* from the outfit's bone names so they match the Base Avatar. <br>*(Ex: If outfit has `Hoodie_Hips`, set Prefix to `Hoodie_` so it matches `Hips`)*. |
| **Unique Bone Prefix** | Text to *add* to bones that **don't** find a match. This prevents naming collisions for non-humanoid bones. |
| **Mesh Prefix** | Added to the name of every mesh object from this outfit. Helps organize the hierarchy. |

#### Animator Settings
| Setting | Description |
| :--- | :--- |
| **Merge Animator (Basic)** | If checked, merges the outfit's Animator Controller layers and parameters into the main avatar. Skips AAS autogenerated layers. |
| **Merge Animator (+AAS Layers)** | If checked, includes AAS autogenerated layers in the merge. |

#### Per-Outfit Fixes
| Setting | Description |
| :--- | :--- |
| **Bounds Fix Mode** | Fix SkinnedMeshRenderer bounds to prevent frustum culling issues.<br>• **None:** Don't modify bounds<br>• **CopyFromSelected:** Copy bounds from reference body mesh<br>• **RecalculateFromMesh:** Recalculate bounds from mesh geometry |
| **Reference Body Mesh** | Body SkinnedMeshRenderer to use as reference for bounds/probe settings. If empty, auto-detects a body mesh. |
| **Probe Anchor Sync Mode** | Sync probe anchor from body mesh.<br>• **None:** Don't modify probe anchors<br>• **AutoDetect:** Automatically find and use body mesh<br>• **CopyFromSelected:** Use specified reference mesh |
| **Reference Probe Anchor Mesh** | Mesh to use as probe anchor reference. If empty, uses referenceBodyMesh or auto-detects. |
| **Force Scale To One** | Force outfit root scale to (1,1,1) before merging to prevent distortion. |
| **Remove Unused Bones** | After merge, delete outfit bones that have no vertex weights and no child objects/components. |

#### Per-Outfit Bone Mapping
| Setting | Description |
| :--- | :--- |
| **Bone Name Mappings** | Optional extra bone mappings for this specific outfit. Overrides global mappings. |

---

### Global Settings

#### 🔗 Global Bone Matching
Advanced bone matching options that apply to all outfits:

| Setting | Description |
| :--- | :--- |
| **Enable Fuzzy Bone Matching** | If exact name matching fails, try mapping list and/or Levenshtein fuzzy matching. |
| **Global Bone Name Mappings** | Global mapping dictionary for bone names (after prefix/suffix stripping). Example: Map 'pelvis' → 'Hips'. |
| **Enable Levenshtein Bone Matching** | Enable fuzzy matching using edit distance algorithm. Useful for typos or slight naming variations. |
| **Max Levenshtein Distance** | Maximum edit distance allowed (1-10). Lower values = stricter matching. |

#### 🎭 Semantic Bone Matching
Use synonyms and patterns to match bones with different naming conventions:

| Setting | Description |
| :--- | :--- |
| **Enable** | Enable semantic bone matching using synonyms and patterns. |
| **Verbose Logging** | Enable detailed logging for semantic bone matching operations. |
| **Synonyms** | List of bone name synonyms (e.g., 'pelvis' → 'Hips', 'upperarm' → 'UpperArm'). |
| **Patterns** | Generic wildcard patterns to match common bones (e.g., `*hips*`, `*spine*`). |
| **Case Insensitive** | Treat pattern matching as case-insensitive. |
| **Enable L/R Variations** | Handle left/right naming variations (e.g., '.L', '_L', 'Left'). |
| **Left Patterns** | Tokens identifying LEFT side bones (default: `.L`, `_L`, ` L`, `Left`, `left`). |
| **Right Patterns** | Tokens identifying RIGHT side bones (default: `.R`, `_R`, ` R`, `Right`, `right`). |

#### 🎬 Animator Merging (Global Improvements)
Advanced animator merging features:

| Setting | Description |
| :--- | :--- |
| **Rewrite Animation Paths** | Automatically rewrite AnimationClip binding paths to match the merged hierarchy. Strips outfit name prefixes from paths. |
| **Merge Avatar Masks** | If multiple incoming layers share the same original name, merge their AvatarMasks (union). |
| **Combine Layers By Name** | Merge layers by original layer name instead of always creating unique (outfit-prefixed) layer names. |

#### 🌐 Global Outfit Defaults
Set default values for all outfits (individual outfit settings override these):

| Setting | Description |
| :--- | :--- |
| **Global Bone Prefix** | Default prefix to strip from bone names. |
| **Global Bone Suffix** | Default suffix to strip from bone names. |
| **Global Bounds Fix Mode** | Default bounds fix mode for all outfits. |
| **Global Reference Body Mesh** | Default body mesh for bounds/probe settings. |
| **Global Probe Anchor Sync Mode** | Default probe anchor sync mode. |
| **Global Reference Probe Anchor Mesh** | Default probe anchor reference mesh. |
| **Global Force Scale To One** | Default: force outfit root scale to (1,1,1). |
| **Global Remove Unused Bones** | Default: remove unused bones after merge. |
| **Global Merge Animator** | Default: merge outfit animators (basic mode). |
| **Global Merge Animator Including AAS** | Default: merge outfit animators including AAS layers. |

**Apply to All Outfits Now** button instantly applies global defaults to all existing outfits.

#### 🧵 Mesh & UV Tools

**UV Validation Settings:**
| Setting | Description |
| :--- | :--- |
| **Enable Verbose Logging** | Enable detailed logging for UV validation and material consolidation operations. |
| **Fill Missing UVs** | If missing UVs are detected, generate simple default UV coordinates. |
| **Auto Fix Overlapping** | Automatically fix overlapping UVs using basic heuristic detection. |
| **Auto Fix Inverted** | Automatically fix inverted UV winding if detected. |

**Material Consolidation Settings:**
| Setting | Description |
| :--- | :--- |
| **Consolidate By Shader And Texture** | Consolidate materials that share the same shader and main texture. |
| **Reuse Existing Materials** | Reuse identical materials across outfits to reduce draw calls. |
| **Merge Duplicate Materials** | Merge duplicate materials found within the same outfit. |
| **Consolidate Materials** | Consolidate materials into shared instances (master toggle). |
| **Match By Name** | Match materials by name similarity using threshold. |
| **Match By Shader** | Match materials by shader type. |
| **Name Similarity Threshold** | Similarity threshold for name-based matching (0-1). Higher = stricter matching. |

#### 🙂 Blend Shape Transfer
Transfer or generate blend shapes between meshes:

**Weight Transfer (Copy Values):**
| Setting | Description |
| :--- | :--- |
| **Enable Verbose Logging** | Enable detailed logging for blend shape transfer operations. |
| **Enable Weight Transfer** | Enable blend shape weight transfer (copy current values between meshes). |
| **Weight Transfer Direction** | Direction: Outfit → Base, Base → Outfit, or Both Directions. |
| **Match By Name** | Match blend shapes by name for weight transfer. |
| **Min Weight Threshold** | Minimum weight threshold to consider for transfer (0-1). |
| **Use Smart Weight Transfer** | Use topology-aware weight transfer algorithm. |

**Blend Shape Generation (Create Frames):**
Create blend shapes on target meshes from source meshes:

| Setting | Description |
| :--- | :--- |
| **Generation Tasks** | List of blend shape generation tasks to execute. |

**Per-Task Settings:**
| Setting | Description |
| :--- | :--- |
| **Enabled** | Enable/disable this generation task. |
| **Source Generation Mesh** | Source mesh to copy blend shapes from. |
| **Blend Shape Names To Generate** | Comma-separated list of blend shape names. Leave empty to generate all. |
| **Generate On Base** | Generate blend shapes on base body mesh. |
| **Generate On Outfits** | Generate blend shapes on specified outfit meshes. |
| **Target Outfit Names** | List of outfit names to generate blend shapes on (if Generate On Outfits is enabled). |
| **Transfer Mode** | Transfer mode: Copy Weights Only or Transfer Frames Approximate. |
| **Max Mapping Distance** | Max vertex-mapping distance for approximate frame transfer (0-0.1 meters). |
| **Use Smart Frame Generation** | Use topology-aware weight scaling for better quality. |
| **Override Existing** | If true, override existing blend shapes. If false, skip if blend shape already exists. |

#### ⛓ Bone Chain Validation
Validate common bone chains for completeness:

| Setting | Description |
| :--- | :--- |
| **Enable Verbose Logging** | Enable detailed logging for bone chain validation. |
| **Enable** | Enable validation of common bone chains (spine, legs, arms). |
| **Warn On Missing** | Log warnings for missing or broken chains. |

#### ✅ Validation (Pre/Post)

**Pre-Merge Validation:**
| Setting | Description |
| :--- | :--- |
| **Enable Verbose Logging** | Enable detailed logging for pre-merge and post-merge validation. |
| **Check Missing Bones** | Check for missing bones referenced by meshes before merge. |
| **Check Mesh Integrity** | Check for null/invalid meshes or components. |

**Post-Merge Verification:**
| Setting | Description |
| :--- | :--- |
| **Check Bounds** | Verify bounds were applied and look sane after merge. |
| **Check Probes** | Verify probe anchor settings were copied where requested. |

#### ⚙ Advanced Settings

**Component Merging:**
| Setting | Description |
| :--- | :--- |
| **Prevent Scale Distortion** | Normalize parent scales before merging to prevent 'exploding' meshes. |
| **Merge Dynamic Bones** | Move Dynamic Bone components to merged hierarchy. |
| **Merge Magica Cloth** | Move Magica Cloth components and trigger data rebuild. |

**CVR Component Merging:**
| Setting | Description |
| :--- | :--- |
| **Merge Advanced Avatar Setup** | Merge Advanced Avatar Settings (AAS) menus and toggles. |
| **Generate AAS Controller At End** | (Highly Recommended) Regenerate the main Animator Controller after all merging to ensure toggles work correctly. |
| **Advanced Settings Prefix** | Add prefix to merged parameter names to avoid collisions (e.g., merging two outfits with same parameter name). |
| **Merge Advanced Pointer Trigger** | Merge CVR Pointer/Trigger components. |
| **Merge Parameter Stream** | Merge CVR Parameter Stream components. |
| **Merge Animator Driver** | Merge CVR Animator Driver components. Auto-splits if >16 parameters. |

**Animator Merging (Master):**
| Setting | Description |
| :--- | :--- |
| **Merge Animator** | Master switch. If disabled, no animators are merged regardless of per-outfit settings. |

#### 🔍 Bone Conflict Resolution

**Global Tolerance Settings:**
| Setting | Description |
| :--- | :--- |
| **Position Threshold** | Position difference (meters) to consider a conflict. Default: 0.001m. |
| **Rotation Threshold** | Rotation difference (degrees) to consider a conflict. Default: 0.5°. |
| **Detect Scale Conflicts** | If enabled, scale differences will be flagged as conflicts. |
| **Scale Threshold** | Scale difference (vector magnitude) to consider a conflict. Default: 0.01. |
| **Default Bone Conflict Resolution** | Default resolution when a mismatch is detected: Still Merge, Constraint To Target, Rename, etc. |

**Conflict Detection:**
1. Click **🔍 Detect Mismatches** button
2. Review detected conflicts in the Bone Conflicts list
3. Choose resolution per bone:
   - **Force Merge (Snap):** Delete outfit bone, move children/weights to avatar bone
   - **Constraint To Target (Safe):** Keep outfit bone, add ParentConstraint to follow avatar bone
   - **Rename (Keep Separate):** Keep bones separate with unique names
   - **Don't Merge (Delete/Ignore):** Skip merging this bone entirely
   - **Merge Into Selected...:** Specify custom target bone

#### 🚫 Exclusions
Exclude specific objects from merge:

| Setting | Description |
| :--- | :--- |
| **Excluded Transforms** | Specific objects to ignore during merge. |
| **Excluded Name Patterns** | Use wildcards (e.g., `*Constraint*`, `*_Ignore`) to skip objects based on name matching. |

#### 🐛 Debug & Logging

| Setting | Description |
| :--- | :--- |
| **Enable Verbose Logging** | Master toggle for detailed merge operation logging. |
| **Log Level** | Granularity: 0=Errors Only, 1=Warnings+Errors, 2=All Details. |

**Per-Category Verbose Logging:**
Each tool category (UV Tools, Blend Shapes, Bone Matching, etc.) has its own verbose logging toggle for fine-grained control.

#### 📊 Preview & Analysis

**Hierarchy Comparison:**
| Setting | Description |
| :--- | :--- |
| **Show Hierarchy Comparison** | Toggle to display side-by-side hierarchy preview showing exact bone matching results. |
| **Max Lines Per Outfit** | Limit preview lines per outfit (50-2000) to prevent UI lag. |

The Hierarchy Comparison shows:
- Current merge settings summary (Fuzzy Matching, Levenshtein, Semantic Matching)
- Side-by-side avatar and outfit armatures
- Match reasons for each bone (exact, global map, semantic synonym, outfit map, levenshtein, semantic pattern, semantic L/R)
- Final bone names after merge
- Unique bones that will be added to avatar
- Per-outfit foldout for detailed inspection

**Merge Statistics Preview:**
| Setting | Description |
| :--- | :--- |
| **Show Preview & Stats** | Display comprehensive merge statistics before build. |
| **🔄 Button** | Refresh statistics cache. |

Preview shows:
- Total outfits, meshes, materials, vertices, triangles
- Bones to merge, constraint, rename, add as unique
- Conflict resolution summary
- Per-outfit breakdown (meshes, materials, bones, conflicts)
- Component counts (AAS, Animator Drivers, Parameter Streams, etc.)
- Animator layer and parameter counts

---

## 🔧 Troubleshooting & Tips

**1. "My clothes are exploding!" (Magica Cloth)**
The plugin attempts to rebuild Magica Cloth data automatically. However, if the mesh order changes significantly, Magica can get confused.
*   **Fix:** Ensure **Merge Magica Cloth** is enabled.
*   **Fix:** Enable **Prevent Scale Distortion** to normalize scales before merging.
*   **Fix:** Try using **Manual Bake** in NDMF settings to inspect the result before upload.

**2. "My animations aren't working."**
*   Check **Generate AAS Controller At End** is enabled.
*   Ensure **Merge Animator** (master switch) is checked.
*   Check the per-outfit **Merge Animator** setting is enabled on the specific outfit.
*   Enable **Rewrite Animation Paths** to automatically fix animation clip paths.
*   If the outfit uses Write Defaults OFF but your avatar uses ON, the plugin attempts to match the Avatar's setting. Check the Console logs for "Write Defaults" warnings.

**3. "Bones are stretching weirdly."**
This happens when the outfit's armature doesn't align perfectly with the base armature.
*   **Fix:** Click **🔍 Detect Mismatches** in the Bone Conflicts section.
*   **Fix:** Find the problematic bone (e.g., `Hips` or `Spine`) and change the resolution to **Constraint To Target**. This keeps the outfit's original bone position but forces it to follow the avatar.
*   **Alternative:** Enable **Semantic Bone Matching** and add synonyms for mismatched bone names.

**4. "UVs are broken/black on merged meshes."**
*   Enable **UV Validation Settings → Fill Missing UVs** to auto-generate missing UV coordinates.
*   Enable **Auto Fix Overlapping** if you see UV overlap issues.
*   Enable **Auto Fix Inverted** if UVs appear flipped.
*   Enable verbose logging for UV validation to see detailed fix reports.

**5. "Materials are duplicated/not consolidating."**
*   Enable **Material Consolidation Settings → Consolidate Materials**.
*   Enable **Reuse Existing Materials** to reduce draw calls.
*   Enable **Merge Duplicate Materials** to combine identical materials within outfits.
*   Adjust **Name Similarity Threshold** if name-based matching is too strict/loose.
*   Enable verbose logging for material consolidation to see which materials are being consolidated.

**6. "Blend shapes are missing/not transferring."**
*   Check **Blend Shape Transfer Settings → Enable Weight Transfer** for copying weights.
*   Ensure **Match By Name** is enabled if blend shape names match.
*   For generating new blend shapes, add a **Generation Task** with the correct source mesh.
*   Set **Max Mapping Distance** higher if vertices aren't mapping correctly (typical: 0.01m).
*   Enable **Use Smart Frame Generation** for better topology-aware results.

**7. "Bones aren't matching with similar names."**
*   Enable **Fuzzy Bone Matching** first (master toggle) in Global Bone Matching.
*   Enable **Levenshtein Bone Matching** for typo tolerance.
*   Set **Max Levenshtein Distance** to 1-2 for similar names (lower = stricter).
*   Use **Semantic Bone Matching** with synonyms for known naming differences (e.g., 'pelvis' → 'Hips').
*   Add wildcard patterns in **Semantic Bone Matching → Patterns** (e.g., `*spine*`, `*arm*`).

**8. "Performance is slow during merge."**
*   Disable verbose logging categories you don't need.
*   Set **Log Level** to 1 (Warnings+Errors) or 0 (Errors Only).
*   Disable **Bone Chain Validation** if you don't need chain completeness checks.
*   Disable **Pre-Merge Validation** and **Post-Merge Verification** if you're confident in your setup.

**9. "External references are broken after merge."**
If you have a script on the Avatar referencing a GameObject inside the Outfit prefab, the **Universal Remapper** will attempt to find the *merged* version of that object and update the reference. If it fails, the reference will be null.
*   **Tip:** Ensure the object names are unique enough to be resolved.
*   **Tip:** Check the Console for remapping warnings.
*   **Tip:** Enable verbose logging to see detailed remapping reports.

**10. "Write Defaults conflicts."**
*   The plugin auto-detects Write Defaults from your base avatar's controller.
*   Check the Console for Write Defaults warnings during merge.
*   Manually ensure all outfit animators use the same Write Defaults setting as your base avatar.

**11. "Hierarchy Comparison shows unexpected matches."**
*   The Hierarchy Comparison preview in the inspector mirrors the exact runtime merge order:
    1. Exact name match (after prefix/suffix stripping)
    2. Global Bone Name Mappings (if Fuzzy Matching enabled)
    3. Semantic synonyms (if Semantic Matching enabled)
    4. Per-outfit bone mappings (overrides earlier)
    5. Levenshtein fuzzy matching (if enabled)
    6. Semantic patterns and L/R variations (if enabled)
*   Enable "Show Hierarchy Comparison" and review the match reasons to understand why bones are being matched.
*   Adjust mapping priority by modifying global vs per-outfit mappings.

---

## � Plugin Functions

The plugin operates in three NDMF build phases:

### BuildPhase.Resolving - Merge Armatures
**Main merging phase that processes all outfits:**

- `ProcessMerger()` - Master orchestrator for outfit merging
- `NormalizeScalesBeforeMerge()` - Prevents exploding meshes by normalizing parent scales
- `MergeOutfitWithoutDestroy()` - Core merge logic for individual outfits
- `BuildBoneMappingWithConflicts()` - Creates bone correspondence between outfit and avatar armatures with conflict resolution
- `FindBodyReferenceMesh()` - Auto-detects body mesh by vertex count for bounds/probe reference
- `ApplyBoundsFix()` - Fixes SkinnedMeshRenderer bounds using selected mode
- `RemoveUnusedBones()` - Cleans up bones with no vertex weights or children
- `ValidateBoneChains()` - Checks spine, leg, and arm bone chains for completeness
- `RunPreMergeValidation()` - Checks for missing bones and mesh integrity before merge
- `RunPostMergeVerification()` - Verifies bounds and probe anchors were applied correctly

**Bone Matching System:**
- `FindBoneByName()` - Exact name matching
- `FindBoneByLevenshtein()` - Fuzzy matching using edit distance algorithm
- `MatchesAnyPattern()` - Wildcard pattern matching (* and ? supported)
- `FindBoneByPatterns()` - Pattern-based bone discovery for semantic matching

### BuildPhase.Transforming - Merge Animators
**Animator merging and CVR component processing:**

- `MergeAnimators()` - Merges animator controllers and parameters
- `MergeAnimatorControllers()` - Combines animator layers from outfits
- `RewriteAnimationPaths()` - Updates animation clip bindings to match merged hierarchy
- `DetectWriteDefaults()` - Auto-detects Write Defaults setting from base controller
- `MergeAvatarMasks()` - Combines avatar masks from layers with matching names
- `MergeAdvancedAvatarSettings()` - Combines AAS menus and parameters
- `MergeAnimatorDriversWithSplit()` - Merges CVR Animator Drivers, auto-splits if >16 parameters
- `MergeParameterStreams()` - Combines CVR Parameter Stream components
- `MergePointerTriggers()` - Merges CVR Pointer/Trigger components

### BuildPhase.Optimizing - Generate AAS Controller At End
**Final controller regeneration:**

- `GenerateAASControllerAtEnd()` - Regenerates main animator controller to ensure all toggles work
- Invokes CVR's `CreateAASController()` method via reflection
- Ensures AAS menu parameters are properly wired to animations
- Creates unique animator controller asset per avatar instance

### Post-Merge Utilities

- `RemapExternalReferencesUniversal()` - Deep reflection-based remapping of external references to merged objects
- `RebuildMagicaData()` - Triggers Magica Cloth data rebuild (MagicaRenderDeformer, VirtualDeformer, BoneCloth, MeshCloth)
- `ValidateUVs()` - Checks and fixes missing/overlapping/inverted UVs
- `ConsolidateMaterials()` - Merges duplicate materials to reduce draw calls
- `TransferBlendShapeWeights()` - Copies blend shape weights between meshes
- `GenerateBlendShapes()` - Creates blend shape frames on target meshes from source

---

## �📋 Advanced Features

### Animation Path Rewriting
When **Rewrite Animation Paths** is enabled, the plugin automatically:
- Strips outfit name prefixes from animation clip binding paths
- Validates new paths exist in the merged hierarchy
- Handles both float curves (transforms, blend shapes) and object reference curves (material swaps, toggles)
- Preserves all keyframe data, tangents, and timing information

Example: `"OutfitName/Armature/Hips"` → `"Armature/Hips"`

### Semantic Bone Matching
Powerful bone matching system that goes beyond exact name matching:
- **Synonyms:** Map `'pelvis'` → `'Hips'`, `'upperarm'` → `'UpperArm'`, etc.
- **Patterns:** Use wildcards like `*spine*`, `*arm*`, `*leg*` to match common bone groups
- **L/R Variations:** Automatically handles `.L`/`.R`, `_L`/`_R`, `Left`/`Right` naming
- **Case Insensitive:** Optional case-insensitive matching

### Material Consolidation
Intelligent material merging reduces draw calls:
- Matches materials by shader type and main texture
- Uses name similarity scoring (configurable threshold)
- Reuses identical materials across multiple outfits
- Merges duplicate materials within single outfits

### Blend Shape Generation
Create blend shapes on outfit meshes from source meshes:
- **Topology Matching:** Maps vertices between meshes using spatial proximity
- **Smart Weight Scaling:** Uses topology-aware algorithms for better quality
- **Selective Generation:** Generate specific blend shapes by name or all at once
- **Multi-Target:** Generate on base body mesh and/or multiple outfit meshes simultaneously

### Preset System
Save and load merge configurations:
- **Save Current as Preset:** Captures current settings (bounds mode, bone mappings, prefixes, etc.)
- **Load Preset:** Applies saved settings to the current component
- **Delete Preset:** Removes saved presets from the list
- Organize by preset name, description, and outfit type
- Presets include: bounds fix mode, probe anchor settings, scale/bone removal flags, bone name mappings, prefixes

### Hierarchy Comparison Preview
Interactive bone matching visualization:
- Shows side-by-side comparison of avatar and outfit armatures
- Displays exact match reasons for each bone (exact, global map, semantic synonym, levenshtein, etc.)
- Shows final bone names after all transformations
- Highlights unique bones that will be added to avatar
- Per-outfit foldout for detailed inspection
- Mirrors exact runtime merge logic for accurate preview

---

## 🎓 Best Practices

### Workflow Tips
1. **Always use Manual Bake first** (in NDMF settings) before uploading to test the result.
2. **Enable verbose logging** for the first merge of a new outfit to catch issues early.
3. **Use Global Outfit Defaults** for consistent settings across all outfits.
4. **Detect Conflicts** before every merge to handle bone mismatches properly.
5. **Enable Generate AAS Controller At End** to ensure toggles work correctly.

### Bone Matching Strategy
1. Start with **exact name matching** (default - after prefix/suffix stripping).
2. If bones don't match, add **Global Bone Name Mappings** for known differences (requires Fuzzy Matching enabled).
3. Enable **Semantic Bone Matching** with synonyms for common naming variations (e.g., 'pelvis' → 'Hips').
4. Use **Levenshtein Fuzzy Matching** for typo tolerance (set max distance 1-3 for best results).
5. For complex armatures with unique names, use wildcard **Patterns** in Semantic Matching.
6. Always use the **Detect Mismatches** button to identify all conflicts and choose appropriate resolutions.

**Bone Matching Order (Runtime):**
1. Check bone conflicts list for explicit resolution
2. Strip prefix/suffix from outfit bone name
3. Check per-outfit bone mappings
4. Apply combined mappings (Global → Semantic Synonyms → Per-Outfit overrides)
5. Try exact name match
6. If Fuzzy Matching + Levenshtein enabled: try fuzzy match
7. If Semantic Matching enabled: try pattern matching + L/R variations

### Performance Optimization
1. Disable **verbose logging** categories you don't actively need.
2. Set **Log Level** to 1 or 0 when not debugging.
3. Use **Material Consolidation** to reduce draw calls on final avatar.
4. Enable **Remove Unused Bones** to clean up hierarchy.
5. Disable **Pre/Post Validation** once you're confident in your setup.

### Magica Cloth Setup
1. Ensure **Merge Magica Cloth** is enabled.
2. Enable **Prevent Scale Distortion** to avoid scale-related explosions.
3. Use **Manual Bake** to inspect cloth data before upload.
4. If cloth still explodes, check for bone conflicts and use **Constraint To Target** resolution.

---
MIT License. See [LICENSE.txt](LICENSE.txt).
