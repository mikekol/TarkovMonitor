# Task 2: Add Zoom Setting to Properties

**Position in project:** Second task — adds the settings property that holds the user's configured zoom level (default 200%). This property is consumed by Task 5 (when sending zoom command) and Task 7 (UI binding).

**Requirement:** Add a new property `zoomLevelOnLocationUpdate` to `TarkovMonitor/Properties/Settings.Designer.cs`. This is an auto-generated file, so you're adding a new property definition following the existing pattern used for `remoteId` and other settings.

**File to Modify:** `TarkovMonitor/Properties/Settings.Designer.cs`

**Exact Code to Add:**

Find the existing `remoteId` property definition (it looks like a `[UserScopedSetting()]` block). After that block, add this new property:

```csharp
[UserScopedSetting()]
[DefaultSettingValue("200")]
public int zoomLevelOnLocationUpdate
{
    get
    {
        object val = this["zoomLevelOnLocationUpdate"];
        if (val == null) return 200;
        return ((int)(val));
    }
    set { this["zoomLevelOnLocationUpdate"] = value; }
}
```

**Steps:**
1. Open `TarkovMonitor/Properties/Settings.Designer.cs`
2. Find the `remoteId` property definition (search for "public string remoteId")
3. After the `remoteId` property block closes, paste the new `zoomLevelOnLocationUpdate` property above
4. Run `dotnet build TarkovMonitor/TarkovMonitor.csproj` to verify it compiles
5. Commit with message: `"feat: add zoomLevelOnLocationUpdate setting (default 200%)"`

**Success Criteria:**
- Property is added to Settings.Designer.cs
- Default value is 200 (not 100 or 400)
- Property getter returns int with null check
- Project compiles without errors
- Commit is made

**Report to:** `Q:\repos\TarkovMonitor\.superpowers\sdd\task-2-report.md`
