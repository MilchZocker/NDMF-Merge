# README.md

```markdown
# NDMF Merge for ChilloutVR

[![Unity 2021.3+](https://img.shields.io/badge/unity-2021.3+-blue.svg)](https://unity3d.com/get-unity/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![ChilloutVR](https://img.shields.io/badge/ChilloutVR-CCK-purple.svg)](https://docs.abinteractive.net/)

A powerful NDMF-based tool for ChilloutVR that provides non-destructive armature and model merging, similar to Modular Avatar but specifically designed for CVR's unique features and workflow.

## 🎯 Why NDMF Merge?

### The Problem

Traditional avatar customization in ChilloutVR has several pain points:

1. **Destructive Workflow**: Manually merging outfits into avatars requires permanently modifying your base avatar
2. **Lost Prefabs**: Once you merge clothing, your prefab connections break, making updates difficult
3. **Time-Consuming**: Every outfit change requires manually remapping bones, adjusting hierarchy, and fixing broken references
4. **Error-Prone**: Manual merging often leads to broken animator paths, missing bone references, and configuration loss
5. **CVR-Specific Components**: Advanced Avatar Settings, Parameter Streams, and Animator Drivers need manual copying and merging

### The Solution

NDMF Merge provides a **non-destructive, build-time workflow** that:

- ✨ **Preserves Prefabs**: Your base avatar and outfits remain as prefabs
- 🔄 **Updates Automatically**: Change an outfit prefab once, all avatars using it update
- 🚀 **Faster Iteration**: Add/remove outfits in seconds, not minutes
- 🛡️ **Error Prevention**: Automatic bone remapping, reference fixing, and conflict detection
- 🎮 **CVR Native**: Full support for ChilloutVR's Advanced Avatar Settings, Parameter Streams, and more

## 🆚 How It Differs from Modular Avatar

| Feature | NDMF Merge (CVR) | Modular Avatar (VRC) |
|---------|------------------|----------------------|
| **Platform** | ChilloutVR | VRChat |
| **CVR Advanced Settings** | ✅ Full merge support | ❌ N/A |
| **CVR Parameter Stream** | ✅ Automatic merging | ❌ N/A |
| **CVR Animator Driver** | ✅ With 16-param splitting | ❌ N/A |
| **Bone Conflict UI** | ✅ Per-bone resolution | ⚠️ Basic |
| **Animator Fixer** | ✅ Detect & auto-fix | ❌ Manual |
| **Assembly References** | ✅ Reflection-based | ✅ Direct |
| **Model Merge Mode** | ✅ Non-armature objects | ✅ Similar |
| **GameObject Target** | ✅ Drag any object | ⚠️ Component only |

### Design Philosophy Differences

**Modular Avatar (VRChat):**
- Designed for VRChat's specific avatar system
- Assumes direct assembly access to VRC SDK
- Focuses on VRChat's constraints and features

**NDMF Merge (ChilloutVR):**
- Built specifically for CVR's flexible avatar system
- Uses reflection to avoid CVR CCK assembly dependencies
- Handles CVR's Advanced Avatar Settings as first-class citizens
- Supports CVR's unique Parameter Stream and Animator Driver systems

## 🎨 How It Improves Your Workflow

### Before NDMF Merge (Traditional Method)

```
1. Import outfit prefab ⏱️ 2 min
2. Manually merge armatures ⏱️ 10 min
3. Remap all bones in skinned meshes ⏱️ 5 min
4. Fix broken animator paths ⏱️ 15 min
5. Copy Advanced Avatar Settings ⏱️ 10 min
6. Merge Parameter Streams ⏱️ 5 min
7. Adjust Animator Drivers ⏱️ 5 min
8. Test and fix issues ⏱️ 10 min
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Total: ~62 minutes per outfit

⚠️ If you need to update the outfit: REPEAT ALL STEPS
⚠️ Base avatar changes: REDO EVERYTHING
```

### After NDMF Merge

```
1. Add CVRMergeArmature component ⏱️ 30 sec
2. Drag outfit into hierarchy ⏱️ 10 sec
3. Upload avatar ⏱️ Auto-processed
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Total: ~40 seconds per outfit

✅ Outfit update: Just update the prefab
✅ Base avatar changes: Automatically propagate
✅ Multiple outfits: Reuse configuration
```

**Time Saved**: ~60 minutes per outfit, **~99% faster** workflow! 🚀

## 🎮 CVR-Specific Features & Handling

### 1. Advanced Avatar Settings (Toggles, Sliders, Joysticks)

**What They Are:**
ChilloutVR's main avatar customization system - allows users to control outfit visibility, colors, facial expressions, and more through the quick menu.

**How NDMF Merge Handles It:**

```
// Automatically merges all Advanced Avatar Settings entries
- Toggles (Bool, Int, Float types)
- Sliders (Material properties, blend shapes)
- Dropdowns (Outfit selection)
- Color Pickers (RGB material control)
- Joysticks (2D/3D position control)
- Input Fields (Single, Vector2, Vector3)

// With smart features:
✅ Prefix support (e.g., "Outfit_Toggle1" to avoid conflicts)
✅ Duplicate detection (prevents name collisions)
✅ Automatic re-initialization (triggers CVR's rebuild)
```

**Example:**
```
Outfit has:
- "Jacket Toggle" → becomes "Outfit_Jacket Toggle"
- "Hood Color" → becomes "Outfit_Hood Color"

Automatically merged into base avatar's Advanced Settings menu
```

### 2. CVR Parameter Stream

**What It Is:**
Allows syncing custom parameters across the network, enabling multiplayer interactions and prop synchronization.

**How NDMF Merge Handles It:**

```
// Finds or creates Parameter Stream on target avatar
// Merges all parameter entries from outfit
// Maintains sync configuration

✅ Automatic component creation if missing
✅ Preserves network sync settings
✅ Handles multiple outfit streams
```

**Why It Matters:**
Your outfit's interactive elements (props, toggles, synced animations) continue working after merge without manual reconfiguration.

### 3. CVR Animator Driver

**What It Is:**
CVR's system for driving animator parameters from external sources (menu items, spawned props, gestures).

**How NDMF Merge Handles It:**

```
// Intelligently merges Animator Drivers with special handling:

✅ Automatic splitting when >16 parameters
   - CVR has a 16-parameter limit per component
   - Creates additional driver components automatically
   - Splits parameter lists into chunks of 16

✅ Entry merging
   - Combines driver entries from outfit to base
   - Maintains trigger conditions
   - Preserves parameter mappings
```

**Example:**
```
Outfit has Animator Driver with 25 parameters:
→ Creates Driver 1 with params 1-16
→ Creates Driver 2 with params 17-25

Automatically split and merged into target avatar
```

### 4. CVR Advanced Avatar Pointer/Trigger

**What They Are:**
- **CVR Pointer**: Raycasting system for interactive elements
- **Advanced Avatar Triggers**: Event-based system for avatar interactions

**How NDMF Merge Handles It:**

```
// Preserves all pointer and trigger components
// Maintains hierarchy and references
// No manual reconfiguration needed

✅ Bone references automatically remapped
✅ Trigger zones move with outfit
✅ Interaction points preserved
```

### 5. Dynamic Bones & Magica Cloth

**ChilloutVR Specifics:**
- Uses standard Dynamic Bones (not VRC PhysBones)
- Supports Magica Cloth 1 & 2 for physics clothing

**How NDMF Merge Handles It:**

```
// Dynamic Bones:
✅ Remaps root bone references
✅ Updates exclusion lists
✅ Preserves all physics settings

// Magica Cloth 1:
✅ Remaps cloth targets
✅ Maintains collision settings

// Magica Cloth 2:
✅ Detects and preserves (uses SerializeReference)
✅ No manual reconfiguration needed
```

### 6. Animator Merging with CVR's System

**CVR's Approach:**
- Base animator controller on CVRAvatar
- Optional override controller
- Advanced Settings can generate additional layers

**How NDMF Merge Handles It:**

```
// Automatically detects animator from:
1. CVRAvatar.overrides field
2. CVRAvatar.avatarSettings.baseController
3. Animator component

// Then merges:
✅ All animator layers (preserves state machines)
✅ All parameters (avoids duplicates)
✅ Layer weights and blend modes
✅ Avatar masks

// With broken reference detection:
✅ Scans all animation clips
✅ Detects missing transform paths
✅ Suggests fixes based on hierarchy
✅ Auto-fix or manual correction
```

## 🔧 Technical Implementation

### Reflection-Based CVR CCK Access

Unlike Modular Avatar which requires direct assembly references, NDMF Merge uses reflection to access CVR CCK components:

```
// Why Reflection?
- CVR CCK doesn't provide assembly definition files
- Keeps NDMF Merge independent of CCK updates
- Allows distribution as standalone UPM package

// How It Works:
1. Finds types at runtime via Assembly.GetType()
2. Accesses fields via reflection
3. Maintains compatibility across CCK versions
```

**Benefits:**
- ✅ No assembly reference errors
- ✅ Works with any CCK version
- ✅ Easier package distribution
- ✅ No breaking changes from CCK updates

### Bone Conflict Resolution System

**The Problem:**
When outfit bones have the same name as avatar bones but different transforms (position/rotation/scale), which one should be used?

**NDMF Merge's Solution:**

```
// Detection Phase (Editor):
1. User clicks "Detect Conflicts"
2. Compares all matching bone names
3. Checks position, rotation, scale differences
4. Lists all conflicts with exact delta values

// Resolution Phase (Per-Bone):
User chooses for EACH conflicting bone:
- Still Merge: Use avatar bone (most common)
- Rename: Keep outfit bone separate (adds "_Merged")
- Don't Merge: Skip this bone entirely

// Build Phase:
Applied automatically during avatar upload
```

**Why Per-Bone Resolution?**
Some bones might need merging (e.g., chest for proper weighting) while others might need separation (e.g., custom props). This gives you complete control.

### Build-Time Processing (NDMF Integration)

```
Upload Avatar
    ↓
Chillaxins triggers NDMF
    ↓
[Resolving Phase]
    → NDMF Merge: Armature & Component Merging
    ↓
[Transforming Phase]  
    → NDMF Merge: Animator Merging
    ↓
[Optimizing Phase]
    → Other NDMF plugins
    ↓
CVR CCK Upload
```

**Key Points:**
- Non-destructive: Original prefabs untouched
- Automatic: No manual steps during upload
- Compatible: Works with other NDMF plugins
- Reversible: Remove component to undo

## 📦 Installation

### Via VCC (Recommended)

1. Add this repository to your VCC listings:
   ```
   https://milchzocker.github.io/NDMF-Merge/index.json
   ```

2. Install **NDMF Merge** from VCC

3. Dependencies installed automatically:
   - NDMF 1.5.0+
   - Chillaxins 1.1.0+

### Via Unity Package Manager

```
1. Window → Package Manager
2. + → Add package from git URL
3. https://github.com/MilchZocker/NDMF-Merge.git
```

### Manual

1. Download latest release
2. Extract to `Packages/dev.milchzocker.ndmf-merge`
3. Ensure NDMF and Chillaxins are installed

## 🚀 Quick Start

### Basic Armature Merge

```
1. Add outfit prefab to your avatar hierarchy
2. Find outfit's root armature (e.g., "Outfit_Armature")
3. Add Component → NDMF Merge → CVR Merge Armature
4. Set "Target Avatar Object" to your base avatar GameObject
5. Upload avatar - merge happens automatically!
```

### With Advanced Avatar Settings

```
1. Outfit has CVRAvatar with Advanced Settings
2. Enable "Merge Advanced Avatar Setup"
3. Optional: Set "Advanced Settings Prefix" (e.g., "Outfit_")
4. Upload - all toggles/sliders merged automatically!
```

### Model Merge (Non-Armature Objects)

```
1. Add prop/accessory to hierarchy
2. Add CVRMergeArmature component
3. Change "Merge Mode" to "Model Merge"
4. Set "Target Bone" (e.g., Head bone for hat)
5. Upload - prop attached with perfect offset!
```

## 🎓 Detailed Guides

### Handling Name Conflicts

**Outfit bones:** `Jacket_Hips`, `Jacket_Spine`, `Jacket_Chest`  
**Avatar bones:** `Hips`, `Spine`, `Chest`

```
Solution:
- Set "Prefix" to "Jacket_"
- NDMF Merge automatically strips it during matching
- Jacket_Hips → matches Hips
```

### Multiple Outfits

```
Avatar Root
├── Base_Armature
├── Jacket_Armature [CVRMergeArmature]
├── Pants_Armature [CVRMergeArmature]
└── Shoes_Armature [CVRMergeArmature]

Each outfit merges independently
All Advanced Settings combined into one menu
```

### Fixing Broken Animator References

```
1. Select outfit GameObject
2. CVRMergeArmature → "Check Animator References"
3. Review detected broken paths
4. Click "Auto-Fix All" or fix individually
5. Broken: "Old/Path/Bone" → Fixed: "Correct/Path/Bone"
```

## 🔍 Troubleshooting

### "Could not find target CVRAvatar"

**Solution:** Drag your avatar's root GameObject (with CVRAvatar component) into "Target Avatar Object" field.

### Bones Not Matching

**Solution:** 
1. Check bone names match after prefix/suffix removal
2. Use "Detect Conflicts" to see mapping
3. Check Console for mapping logs

### Advanced Settings Not Appearing

**Solution:**
1. Ensure "Merge Advanced Avatar Setup" is enabled
2. Check outfit has CVRAvatar component with settings
3. Verify no name conflicts (use prefix)

### Components Not Remapping

**Solution:**
1. Enable specific merge options:
   - Merge Dynamic Bones
   - Merge Magica Cloth
   - Merge Advanced Pointer Trigger
2. Check component types are supported

## 🤝 Contributing

Contributions welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Setup

```
git clone https://github.com/MilchZocker/NDMF-Merge.git
cd NDMF-Merge
# Open in Unity 2021.3+
# Install NDMF and Chillaxins via VCC
```

### Reporting Issues

Please include:
- Unity version
- CVR CCK version
- NDMF Merge version
- Console logs
- Steps to reproduce

## 📄 License

This project is licensed under the MIT License - see [LICENSE.md](LICENSE.md) for details.

## 🙏 Credits

- **Built with:** [NDMF](https://github.com/bdunderscore/ndmf) by bd_
- **Designed for:** [Chillaxins](https://docs.hai-vr.dev/docs/products/chillaxins) by Haï~
- **Inspired by:** [Modular Avatar](https://modular-avatar.nadena.dev/) by bd_
- **Platform:** [ChilloutVR](https://store.steampowered.com/app/661130/ChilloutVR/) by Alpha Blend Interactive

## 📞 Support

- **Issues:** [GitHub Issues](https://github.com/MilchZocker/NDMF-Merge/issues)
- **Discussions:** [GitHub Discussions](https://github.com/MilchZocker/NDMF-Merge/discussions)
- **Discord:** [ChilloutVR Official](https://discord.gg/abi)

---

**Made with ❤️ for the ChilloutVR community**
```

## CONTRIBUTING.md

```markdown
# Contributing to NDMF Merge

Thank you for your interest in contributing to NDMF Merge! This document provides guidelines and information for contributors.

## Code of Conduct

- Be respectful and inclusive
- Provide constructive feedback
- Focus on what is best for the community

## Development Setup

### Prerequisites

- Unity 2021.3 or later
- ChilloutVR CCK (latest version)
- NDMF 1.5.0+
- Chillaxins 1.1.0+

### Local Development

1. Fork the repository
2. Clone your fork:
   ```
   git clone https://github.com/YOUR_USERNAME/NDMF-Merge.git
   ```
3. Create a feature branch:
   ```
   git checkout -b feature/your-feature-name
   ```

## Making Changes

### Code Style

- Follow C# naming conventions
- Use meaningful variable names
- Comment complex logic
- Keep methods focused and small

### Testing

Before submitting:
1. Test armature merge with various avatars
2. Test CVR component merging
3. Test animator merging
4. Verify no console errors
5. Check with multiple outfit combinations

### Commit Messages

Use clear, descriptive commit messages:
```
feat: Add support for CVR spawn points
fix: Resolve bone conflict detection issue
docs: Update installation instructions
refactor: Improve animator merging performance
```

## Submitting Changes

1. Push to your fork:
   ```
   git push origin feature/your-feature-name
   ```

2. Create a Pull Request with:
   - Clear description of changes
   - Why the change is needed
   - Testing performed
   - Screenshots (if UI changes)

## Reporting Bugs

Include in bug reports:
- Unity version
- CVR CCK version
- NDMF Merge version
- Steps to reproduce
- Expected vs actual behavior
- Console logs
- Screenshots

## Feature Requests

When requesting features:
- Describe the use case
- Explain expected behavior
- Consider implementation complexity
- Check existing issues first

## Questions?

Feel free to:
- Open a GitHub Discussion
- Ask in ChilloutVR Discord
- Review existing documentation

Thank you for contributing! 🎉
```

This documentation comprehensively covers why NDMF Merge exists, how it differs from Modular Avatar, all CVR-specific handling, and provides practical workflow improvements with concrete time savings!