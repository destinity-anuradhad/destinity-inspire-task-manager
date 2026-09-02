using System.ComponentModel.DataAnnotations.Schema;

namespace TaskTracker.Models;

[Table("CustomFieldDefinitions")]
public class CustomFieldDefinition
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string Name { get; set; } = "";
    // Text | Number | Date | Boolean | Select
    public string FieldType { get; set; } = "Text";
    public bool IsRequired { get; set; }
    // JSON array; used only for Select fields.
    public string? OptionsJson { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

[Table("TaskCustomFieldValues")]
public class TaskCustomFieldValue
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int CustomFieldDefinitionId { get; set; }
    // Stored canonically: invariant number, yyyy-MM-dd date, true/false boolean, or text/select value.
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}
