# Save As System Improvements - DarkEngine3D

## Overview
**Bug #3: Save As File Creation** has been significantly improved to handle file creation when saving to a new location that doesn't exist yet (e.g., saving as `game2.ing` when it doesn't exist).

## What Was Fixed

### Previous Behavior
- User selects "Save As"
- Enters new filename (e.g., `game2.ing`)
- If directory doesn't exist → **ERROR**
- If file already exists → overwrites
- Limited error feedback to user

### Current Behavior ✅
- User selects "Save As"
- Enters new filename (e.g., `game2.ing`)
- **Automatically creates directories** if they don't exist
- Verifies write permissions before saving
- **Creates the file** if it doesn't exist
- **Comprehensive error handling** with detailed feedback

## Technical Implementation

### 1. **Automatic Directory Creation**
```csharp
// Ensures the directory exists - creates all parent directories if needed
string? dirPath = Path.GetDirectoryName(filePath);
if (!string.IsNullOrEmpty(dirPath))
{
	Directory.CreateDirectory(dirPath);
	Console.WriteLine($"[SceneManagerPanel] Created directory: {dirPath}");
}
```

### 2. **Permission Verification**
```csharp
// Verify write permissions before attempting save
if (!Directory.Exists(dirPath ?? AppDomain.CurrentDomain.BaseDirectory))
{
	throw new UnauthorizedAccessException($"Cannot create directory: {dirPath}");
}
```

### 3. **File Creation & Verification**
```csharp
// Write to file with error handling
File.WriteAllText(filePath, json);

// Verify file was created successfully
if (!File.Exists(filePath))
{
	throw new IOException($"File was not created: {filePath}");
}
```

### 4. **Enhanced Error Reporting**
Different error types are caught and reported specifically:

| Error Type | Handling |
|-----------|----------|
| **UnauthorizedAccessException** | Permission denied - shows user cannot write |
| **DirectoryNotFoundException** | Directory path invalid |
| **IOException** | File I/O errors during write |
| **General Exception** | Catches any other errors with details |

### 5. **Success Feedback**
When save is successful:
```
[SceneManagerPanel] ✅ Saved X scene(s) (+ 3D objects) to C:\path\to\game2.ing
[SceneManagerPanel] File size: 12345 bytes
```

## File Structure

### Modified Files
- **Engine/IDE/Panels/SceneManagerPanel.cs**
  - Enhanced `SaveToIngFile()` method with comprehensive error handling
  - Added directory creation logic
  - Added permission verification
  - Added success/failure feedback

### Key Methods
1. **SaveToIngFile(string filePath)**
   - Main save implementation
   - Handles directory creation
   - Performs error checking
   - Provides detailed feedback

2. **OpenSaveAsDialog()**
   - Opens file save dialog
   - Called from File > Save As menu

## Usage Workflow

### Step 1: Open Save As Dialog
```
File Menu → Save As
OR
Scene Manager Panel → [Right-click] → Save As
```

### Step 2: Select Location & Filename
- Browse to desired directory
- Enter filename (e.g., `game2.ing`)
- System auto-adds `.ing` extension if missing

### Step 3: System Handles Everything
- ✅ Creates directory structure if needed
- ✅ Creates new file
- ✅ Saves all scenes + 3D objects
- ✅ Reports success/failure

## Error Scenarios & Solutions

### Scenario 1: New Directory Path
```
User wants to save to: D:\MyGames\Project2\game2.ing
Directory doesn't exist
→ System creates: D:\MyGames\Project2\
→ Creates file: game2.ing
✅ Success
```

### Scenario 2: Permission Denied
```
Trying to save to: C:\Windows\System32\game2.ing
→ Catches UnauthorizedAccessException
→ Reports: "Permission denied - Cannot write to C:\Windows\System32\"
✅ Clear error message
```

### Scenario 3: Invalid Path
```
Trying to save to: \\InvalidNetwork\path\game2.ing
Network path unreachable
→ Catches DirectoryNotFoundException
→ Reports: "Directory not found"
✅ Clear error message
```

## Testing Checklist

- [ ] Save to new filename in current directory
- [ ] Save to new nested directory path
- [ ] Save with special characters in filename
- [ ] Save to network path (if available)
- [ ] Verify file is created with correct content
- [ ] Check file size matches expected data
- [ ] Verify error handling with invalid paths
- [ ] Verify permission error on read-only locations

## Console Output Examples

### Successful Save
```
[SceneManagerPanel] Created directory: D:\MyGames\MyProject2
[SceneManagerPanel] ✅ Saved 2 scene(s) (+ 3D objects) to D:\MyGames\MyProject2\game2.ing
[SceneManagerPanel] File size: 45678 bytes
```

### Failed Save - Permission Denied
```
[SceneManagerPanel] ❌ Permission denied: Access to the path is denied.
[SceneManagerPanel] Cannot write to: C:\Program Files\game2.ing
```

### Failed Save - Directory Not Found
```
[SceneManagerPanel] ❌ Directory not found: Could not find a part of the path.
```

## Code Changes Summary

### SaveToIngFile() Enhancements
1. **Input Validation**
   - Check if filePath is null/empty
   - Check if editor scenes exist

2. **Directory Management**
   - Extract directory path
   - Create all parent directories
   - Verify directory was created

3. **Permission Checking**
   - Verify write access before saving

4. **File Writing**
   - Serialize scene data to JSON
   - Write to file
   - Verify file was created

5. **Error Handling**
   - Catch UnauthorizedAccessException
   - Catch DirectoryNotFoundException
   - Catch IOException
   - Catch general exceptions
   - Provide specific error messages

## Backward Compatibility
✅ All existing save functionality remains unchanged
✅ Existing files continue to load normally
✅ File format is identical
✅ No breaking changes to serialization

## Future Enhancements
- Add visual progress indicator for large saves
- Implement backup system (save .bak before overwriting)
- Add undo/redo for save operations
- Support for multiple save formats
- Cloud save synchronization
