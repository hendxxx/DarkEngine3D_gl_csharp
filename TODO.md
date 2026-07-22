# Exit Confirm Dialog Implementation

## Steps
1. ✅ HierarchyPanel.cs - Fix: Render ALL children (including hidden dialog buttons)
2. ✅ Update `game.ing` - Add ExitConfirm Dialog (Yes/Cancel) with isVisible: false
3. ✅ Update `Main Menu.ing` - Add ExitConfirm Dialog (Yes/Cancel) with isVisible: false
4. ✅ Modify `MainMenuScene.cs` - confirmexit behavior: preview mode → stop preview, only IDE off → exit
