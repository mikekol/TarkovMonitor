# Task 7: Add Zoom Level Slider to Settings.razor

**Position in project:** Seventh task — adds UI controls to the Settings page for users to configure the zoom level (and optionally view/edit remote IDs). This exposes the settings added in Task 2 to the user interface.

**Dependencies:** Requires Task 2 (zoom setting property).

**File to Modify:** `TarkovMonitor/Settings.razor`

**Exact Code to Add:**

Find a good location in the settings form (near the bottom or in a dedicated section). Add this HTML section:

```razor
<div class="settings-section">
    <h3>Browser Remote Control</h3>
    
    <div class="setting-item">
        <label for="remoteIds">Remote Browser IDs (comma-separated for multiple):</label>
        <input type="text" 
               id="remoteIds" 
               @bind="@remoteIdInput"
               placeholder="e.g., abc123,def456"
               class="form-control" />
        <small class="form-text text-muted">
            Enter one or more remote IDs from tarkov.dev, separated by commas.
        </small>
    </div>
    
    <div class="setting-item">
        <label for="zoomLevel">Zoom level when location is detected via screenshot (%):</label>
        <input type="range" 
               id="zoomLevel" 
               min="100" 
               max="400" 
               step="25"
               @bind="@zoomLevelInput"
               class="form-range" />
        <span>@zoomLevelInput%</span>
        <small class="form-text text-muted">
            Adjust how much the map zooms in when a screenshot reveals your player position.
        </small>
    </div>
</div>
```

In the `@code` block of Settings.razor, add these properties and methods:

```csharp
private string remoteIdInput = "";
private int zoomLevelInput = 200;

protected override void OnInitialized()
{
    remoteIdInput = Properties.Settings.Default.remoteId ?? "";
    zoomLevelInput = Properties.Settings.Default.zoomLevelOnLocationUpdate;
}

private void SaveRemoteSettings()
{
    Properties.Settings.Default.remoteId = remoteIdInput;
    Properties.Settings.Default.zoomLevelOnLocationUpdate = zoomLevelInput;
    Properties.Settings.Default.Save();
}
```

If there's already a Save button on the form, make sure it calls `SaveRemoteSettings()`. If not, add:

```razor
<button @onclick="SaveRemoteSettings" class="btn btn-primary">Save Settings</button>
```

**Steps:**
1. Open `TarkovMonitor/Settings.razor`
2. Locate the HTML form area (where other settings are displayed)
3. Add the "Browser Remote Control" section with both input fields above
4. In the `@code` block at the bottom, add the private fields and methods above
5. In `OnInitialized()`, initialize the fields from Settings
6. Update the Save button (or add one) to call `SaveRemoteSettings()`
7. Run `dotnet build TarkovMonitor/TarkovMonitor.csproj` to verify it compiles
8. Commit with message: `"feat: add zoom level slider and remote ID input to Settings UI"`

**Success Criteria:**
- HTML section displays with heading "Browser Remote Control"
- Remote ID input field is present with placeholder text
- Zoom level slider is present with range 100-400, step 25
- Current zoom value is displayed next to the slider (e.g., "200%")
- `@bind` bindings connect UI to component properties
- `OnInitialized()` loads current values from `Properties.Settings.Default`
- `SaveRemoteSettings()` persists changes to settings and calls `.Save()`
- Project compiles without errors
- Commit is made

**UI Behavior:**
- Remote ID field shows comma-separated list of currently configured remotes
- Zoom slider shows current zoom level percentage
- Changes are saved when user clicks the Save button
- Settings persist across application restarts

**Report to:** `Q:\repos\TarkovMonitor\.superpowers\sdd\task-7-report.md`
