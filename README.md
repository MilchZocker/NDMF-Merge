# NDMF Merge for ChilloutVR

**NDMF Merge** is a powerful, non-destructive tool for ChilloutVR (CVR) that merges outfits, accessories, and armatures onto your base avatar at build time.

It allows you to keep your project clean by keeping outfits as separate prefabs. When you upload or enter Play Mode, the tool clones your avatar, grafts the outfits, handles bone merging, rebuilds physics, and generates the necessary controllers—all without touching your source files.

> **Recommended:** Use this alongside [NDMF-Avatar-Optimizer](https://github.com/bdunderscore/ndmf-avatar-optimizer). NDMF Merge combines the avatars, and Avatar Optimizer cleans up the resulting hierarchy.

## ✨ Key Features

*   **Non-Destructive Workflow:** Your source assets remain untouched. All merging happens on a temporary clone during the build process.
*   **Universal Reference Remapper (v2):** Uses a deep reflection-based sweep to find scripts referencing the *old* outfit objects and automatically points them to the *new* merged avatar. This fixes broken scripts and missing references on custom components.
*   **Smart Bone Merging:**
    *   **Merge:** Snaps outfit bones to the avatar's armature.
    *   **Constraint Mode:** Can resolve bone conflicts by creating a **ParentConstraint** with zero offset. Great for outfits that don't perfectly align with the base armature but need to follow it.
    *   **Rename/Unique:** Keeps bones separate if they shouldn't merge.
*   **Intelligent Animator Merging:**
    *   Merges Animator Controllers from outfits.
    *   **Auto-detects Write Defaults:** Scans your base avatar's controller to enforce consistent Write Defaults settings on merged layers.
    *   Splits **Animator Drivers** automatically if they exceed the 16-parameter limit.
*   **Full CVR Component Support:**
    *   Merges **Advanced Avatar Settings (AAS)**, creating a unified menu.
    *   Merges **Parameter Streams** and **Pointer/Trigger** components.
    *   Regenerates the AAS Animator Controller at the end of the build to ensure all animations allow for toggling.
*   **Physics Support:**
    *   Supports Dynamic Bones.
    *   **Magica Cloth Integration:** Automatically triggers a data rebuild for `MagicaRenderDeformer`, `VirtualDeformer`, `BoneCloth`, and `MeshCloth` after merging to prevent "exploding" mesh issues.

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
Add the package via the Package Manager or copy the `NDMF-Merge` folder into your project's `Packages/` directory.

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
1.  Click the **Detect Conflicts in All Outfits** button.
2.  The tool will scan for bones that exist in both the Base Avatar and the Outfit.
3.  Scroll down to **Bone Conflicts**. You will see a list of matching bones.
4.  **Review Resolutions:**
    *   **Still Merge (Default):** The outfit bone is deleted, and its children/weights are moved to the Base Avatar's bone.
    *   **Constraint To Target:** The outfit bone is kept, moved to the root, and **Parent Constrained** to the base bone. Use this if the merge causes deformation because the bones aren't in the exact same spot.
    *   **Rename / Don't Merge:** Keeps the bone entirely separate.

### 6. Bake
Simply upload your avatar to test. NDMF runs automatically, or Try using **Manual Bake** in NDMF settings to inspect the result before upload (recommended for magica cloth setups)

---

## ⚙️ Detailed Configuration

### Outfit Settings
For each entry in the "Outfits to Merge" list:

| Setting | Description |
| :--- | :--- |
| **Outfit** | The GameObject (prefab) of the clothes/props you want to add. |
| **Prefix / Suffix** | Text to *remove* from the outfit's bone names so they match the Base Avatar. <br>*(Ex: If outfit has `Hoodie_Hips`, set Prefix to `Hoodie_` so it matches `Hips`)*. |
| **Unique Bone Prefix** | Text to *add* to bones that **don't** find a match. This prevents naming collisions for non-humanoid bones. |
| **Mesh Prefix** | Added to the name of every mesh object from this outfit. Helps organize the hierarchy. |
| **Merge Animator** | If checked, merges the outfit's Animator Controller layers and parameters into the main avatar. |
| **Include AAS Auto-Layers** | If unchecked, skips layers auto-generated by CCK (prevents duplicates). |

### Global Settings

*   **Merge Advanced Avatar Settings:**
    *   Merges menus and toggles.
    *   **Generate AAS Controller At End:** (Highly Recommended) Forces the plugin to regenerate the main Animator Controller *after* all merging is done. This ensures that toggles for merged items work correctly.
    *   **Advanced Settings Prefix:** Adds a prefix to merged parameter names to avoid collisions (e.g., merging two outfits that both use a parameter named "Toggle").

*   **Component Merging:**
    *   **Lock Parent Scale:** Forces parent scales to (1,1,1) to prevent distortion when parenting outfit bones.
    *   **Merge Dynamic Bones / Magica Cloth:** Moves these components to the new hierarchy. *Note: Magica Cloth data is auto-rebuilt during this process.*
    *   **Merge Animator Driver:** Merges drivers. If a driver has >16 parameters, it splits it into multiple components to satisfy CCK limits.

*   **Exclusions:**
    *   **Excluded Transforms:** Specific objects to ignore during the merge.
    *   **Excluded Name Patterns:** Use wildcards (e.g., `*Constraint*`) to skip objects based on name.

---

## 🔧 Troubleshooting & Tips

**1. "My clothes are exploding!" (Magica Cloth)**
The plugin attempts to rebuild Magica Cloth data automatically. However, if the mesh order changes significantly, Magica can get confused.
*   **Fix:** Ensure **Merge Magica Cloth** is enabled.
*   **Fix:** Try using **Manual Bake** in NDMF settings to inspect the result before upload.

**2. "My animations aren't working."**
*   Check **Generate AAS Controller At End**.
*   Ensure **Merge Animator** is checked on the specific outfit.
*   If the outfit uses Write Defaults OFF but your avatar uses ON, the plugin attempts to match the Avatar's setting. Check the Console logs for "Write Defaults" warnings.

**3. "Bones are stretching weirdly."**
This happens when the outfit's armature doesn't align perfectly with the base armature.
*   **Fix:** In the **Bone Conflicts** list, find the problematic bone (e.g., `Hips` or `Spine`) and change the resolution to **Constraint To Target**. This keeps the outfit's original bone position but forces it to follow the avatar.

**4. External References**
If you have a script on the Avatar referencing a Game Object inside the Outfit prefab, the **Universal Remapper** will attempt to find the *merged* version of that object and update the reference. If it fails, the reference will be null.
*   **Tip:** Ensure the object names match or are unique enough to be resolved.

## License
MIT License. See [LICENSE.txt](LICENSE.txt).
