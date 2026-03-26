# Contract: Instance Reference Template (Blueprint Schema)

## Blueprint JSON Addition

The `instanceReference` property is added at the top level of the blueprint definition, alongside `title`, `participants`, `actions`, etc.

```json
{
  "title": "Construction Permit Approval",
  "participants": [...],
  "actions": [...],
  "instanceReference": {
    "prefix": "CP",
    "components": [
      {
        "field": "/projectName",
        "transform": "first-word",
        "chars": 3
      },
      {
        "field": "/siteAddress",
        "transform": "first-word",
        "chars": 3
      }
    ]
  }
}
```

## Generated Reference Format

```
{PREFIX}-{COMP1}-{COMP2}-{HASH}
```

All segments are uppercase. Separator is hyphen.

### Examples

| Blueprint Config | Action 1 Data | Generated Reference |
|-----------------|---------------|-------------------|
| prefix: "CP", projectName first-word 3, siteAddress first-word 3 | projectName: "Riverside Heights", siteAddress: "14 Waterfront Lane" | CP-RIV-14W-a7k3 |
| prefix: "CP", projectName first-word 3, siteAddress first-word 3 | projectName: "Central Business Tower", siteAddress: "100 High Street" | CP-CEN-100-b2m9 |
| prefix: "PA", applicantName truncate 4 | applicantName: "Smith" | PA-SMIT-c4x1 |

### Fallback (no template defined)

```
{FIRST_2_CHARS_OF_TITLE}-{4_CHAR_HASH}
```

Example: Blueprint title "Planning Application" → `PL-a7k3`

## Transform Rules

| Transform | Input | Chars | Output |
|-----------|-------|-------|--------|
| first-word | "Riverside Heights" | 3 | "RIV" |
| first-word | "14 Waterfront Lane" | 3 | "14W" |
| truncate | "Smith" | 4 | "SMIT" |
| uppercase | "riverside" | 10 | "RIVERSIDE" |

### Edge Cases

| Input | Transform | Output |
|-------|-----------|--------|
| null | any | "UNK" |
| "" | any | "UNK" |
| "A" | first-word, chars=3 | "A" (no padding) |
| "Cafe Creme" | first-word, chars=5 | "CAFE" (first word only) |

## Validation Rules

- `prefix`: Required, 1-5 uppercase alpha characters, regex `^[A-Z]{1,5}$`
- `components`: Required, 1-5 items
- `components[].field`: Required, valid JSON Pointer starting with "/"
- `components[].transform`: Required, one of: "first-word", "truncate", "uppercase"
- `components[].chars`: Required, integer 2-10, default 3
- Generated reference max length: 30 characters (prefix + components + hash + separators)
