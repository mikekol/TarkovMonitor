# Task 1: Create BrowserRemote Class

**Position in project:** First task — creates foundational class for multi-remote support. This class is used by later tasks (Task 3 onwards) to represent individual remote instances.

**Requirement:** Create a new simple class `BrowserRemote` in file `TarkovMonitor/BrowserRemote.cs` that holds a remote ID string. This is a lightweight holder with one property (`Id`).

**Exact Code to Write:**

```csharp
namespace TarkovMonitor
{
    /// <summary>
    /// Represents a single tarkov.dev browser remote instance.
    /// </summary>
    public class BrowserRemote
    {
        /// <summary>
        /// The unique session ID for this remote (e.g., "abc123").
        /// </summary>
        public string Id { get; set; } = string.Empty;
    }
}
```

**Steps:**
1. Create file `TarkovMonitor/BrowserRemote.cs` with the exact code above
2. Run `dotnet build TarkovMonitor/TarkovMonitor.csproj` to verify it compiles
3. Commit with message: `"feat: add BrowserRemote class for representing remote instances"`

**Success Criteria:**
- File exists and contains the class with `Id` property
- Project compiles without errors
- Commit is made

**Report to:** `Q:\repos\TarkovMonitor\.superpowers\sdd\task-1-report.md`
