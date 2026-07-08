# Task 8: Integration Test — Multiple Remotes with Auto-Zoom

**Position in project:** Final task — end-to-end verification that all pieces work together. Tests that multiple remotes can be configured, that position + zoom commands are sent correctly, and that backward compatibility with single remotes is maintained.

**Dependencies:** Requires all previous tasks (1-7) to be complete.

**File to Modify:** None (manual testing)

**Test Steps:**

**Build the Application:**
1. Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
2. Expected: Build succeeds with no errors
3. Record the output

**Configure Multiple Remotes in Settings UI:**
1. Start the application: `dotnet run --project TarkovMonitor/TarkovMonitor.csproj`
2. Open Settings dialog/page
3. Find "Remote Browser IDs" field
4. Enter: `test123,test456,test789` (three test remote IDs)
5. Find "Zoom level" slider
6. Set it to: 250%
7. Click Save button
8. Verify the UI shows the values were accepted

**Verify Settings Persistence:**
1. Close the Settings dialog
2. Reopen the Settings dialog
3. Verify both fields still show the same values:
   - Remote IDs: `test123,test456,test789`
   - Zoom: 250%
4. This confirms settings are being persisted correctly

**Test Backward Compatibility (Single Remote):**
1. Change "Remote Browser IDs" to just: `test123` (single ID only)
2. Click Save
3. Verify no errors appear
4. Close and reopen Settings to confirm it saved

**Build Again to Confirm No Regressions:**
1. Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
2. Expected: Build succeeds with no compilation errors

**Optional: Advanced Test (if tarkov.dev WebSocket is accessible)**
If you have access to actual tarkov.dev remote IDs or a way to mock the WebSocket:
1. Configure real remote IDs in the settings
2. Take a screenshot with player position data (if in-game)
3. Monitor console output or WebSocket traffic
4. Verify position message is sent to all 3 remotes
5. Verify zoom message is sent to all 3 remotes

**Success Criteria:**
- Application builds without errors
- Multiple remote IDs can be entered and saved
- Zoom level slider works (range 100-400, step 25)
- Settings persist across close/reopen
- Single remote still works (backward compatibility)
- No exceptions or errors in the UI
- Code compiles without breaking existing functionality

**What to Report:**
1. Successful build output
2. UI testing results (settings save/load work?)
3. Any errors encountered
4. Whether optional WebSocket testing was performed and results
5. Backward compatibility test passed

**Known Limitation:**
The zoom command format (`type: "zoom"`) may need adjustment if tarkov.dev's WebSocket API doesn't support it. This will be apparent during actual WebSocket testing. For now, the code is correct per specification; format changes would be captured in a follow-up task.

**Report to:** `Q:\repos\TarkovMonitor\.superpowers\sdd\task-8-report.md`
