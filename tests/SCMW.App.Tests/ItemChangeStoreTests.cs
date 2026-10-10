using Avalonia.Headless.XUnit;
using SCMW.Core.Editing;

namespace SCMW.App.Tests;

public class ItemChangeStoreTests
{
    [AvaloniaFact]
    public void Recording_writes_a_change_into_the_project_folder()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var store = new ItemChangeStore(new ProjectChangeStore(), projects);
            var project = projects.Current!;

            store.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id);

            Assert.True(File.Exists(Path.Combine(project.FolderPath, ProjectChangeStore.FileName)));
            var record = Assert.Single(store.For(project.FolderPath));
            Assert.Equal("Duct Tape", record.AssetId);
            Assert.Equal("Max stack", record.FieldTechnicalName);
            Assert.Equal("10", record.OldValue);
            Assert.Equal("25", record.NewValue);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void For_is_empty_when_the_project_has_no_records()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var store = new ItemChangeStore(new ProjectChangeStore(), projects);

            Assert.Empty(store.For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void AppliedValue_returns_the_latest_new_value_for_a_field()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var store = new ItemChangeStore(new ProjectChangeStore(), projects);
            var project = projects.Current!;

            store.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id);
            store.RecordEdit("Duct Tape", "Max stack", "10", "30", project.Id);

            Assert.Equal("30", store.AppliedValue(project.FolderPath, "Duct Tape", "Max stack", "10"));
            Assert.Equal("42", store.AppliedValue(project.FolderPath, "Military Backpack", "Storage slots", "42"));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void A_second_record_for_the_same_field_shows_the_newer_value()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var store = new ItemChangeStore(new ProjectChangeStore(), projects);
            var project = projects.Current!;

            store.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id);
            store.RecordEdit("Duct Tape", "Max stack", "10", "30", project.Id);

            var records = store.For(project.FolderPath);
            Assert.Equal(2, records.Count);
            Assert.Equal("25", records[0].NewValue);
            Assert.Equal("30", records[^1].NewValue);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Changed_fires_when_a_record_is_added()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var store = new ItemChangeStore(new ProjectChangeStore(), projects);
            var fired = 0;
            store.Changed += () => fired++;

            store.RecordEdit("Duct Tape", "Max stack", "10", "25", projects.Current!.Id);

            Assert.Equal(1, fired);
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Recording_with_an_unknown_project_records_nothing()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var store = new ItemChangeStore(new ProjectChangeStore(), projects);

            store.RecordEdit("Duct Tape", "Max stack", "10", "25", "no-such-project");

            Assert.Empty(store.For(projects.Current!.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }

    [AvaloniaFact]
    public void Undo_removes_the_record()
    {
        var (projects, root) = TestProjects.With("My first mod");
        try
        {
            var store = new ItemChangeStore(new ProjectChangeStore(), projects);
            var project = projects.Current!;
            store.RecordEdit("Duct Tape", "Max stack", "10", "25", project.Id);
            var record = Assert.Single(store.For(project.FolderPath));

            var error = store.Undo(project.FolderPath, record.Id);

            Assert.Null(error);
            Assert.Empty(store.For(project.FolderPath));
        }
        finally { TestProjects.Cleanup(root); }
    }
}
