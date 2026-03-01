# Blueprint Designer Application - Design Plan

## Overview

A Blazor WebAssembly application for visually designing, validating, and testing Blueprint workflows. The designer will provide a graphical canvas for creating multi-party workflows with drag-and-drop interaction, supporting both desktop and mobile/tablet devices.

## Core Requirements

### Functional Requirements
- **Visual Workflow Design**: Graphical canvas for creating blueprints with drag-and-drop
- **Participant Management**: Add, configure, and visualize workflow participants
- **Action Configuration**: Define actions with data schemas, routing logic, and UI forms
- **Data Disclosure Rules**: Visual configuration of selective data visibility
- **JSON Logic Editor**: Build conditional routing and calculations visually
- **Validation**: Real-time validation using Fluent API and JSON Schema
- **Import/Export**: Load and save blueprints as JSON
- **Preview/Test**: Simulate workflow execution

### Technical Requirements
- **Blazor WebAssembly (.NET 10)**: Client-side execution, no server required
- **Anonymous Access**: No authentication initially (add later)
- **Mobile-Friendly**: Touch support for phone/tablet
- **Offline Capable**: PWA with service worker support
- **Fast Loading**: Code splitting and lazy loading

---

## Design Options

### Option 1: Blazor.Diagrams (Recommended)
**Library**: [Blazor.Diagrams](https://github.com/Blazor-Diagrams/Blazor.Diagrams)

#### Advantages
- ✅ **Fully Open Source** (MIT License) - No licensing costs
- ✅ **Pure C#/Blazor** - 95% C#, minimal JavaScript
- ✅ **Highly Customizable** - Complete control over appearance and behavior
- ✅ **Modern Architecture** - Built specifically for Blazor, not a wrapper
- ✅ **Active Development** - Well-maintained with recent updates
- ✅ **Extensible** - Easy to add custom nodes, ports, and behaviors
- ✅ **Performance** - Optimized for large diagrams with virtualization support

#### Diagram Features
- Node-based workflow visualization
- Connectors/links between actions
- Custom node rendering (participants, actions, data schemas)
- Pan and zoom canvas
- Grid/snap-to-grid
- Multi-select and bulk operations
- Grouping and containers

#### Architecture
```
┌─────────────────────────────────────────────────────┐
│              Blazor.Diagrams Canvas                  │
│                                                      │
│  ┌──────────────┐        ┌──────────────┐          │
│  │ Participant  │───────▶│   Action 1   │          │
│  │   (Alice)    │        │  "Submit"    │          │
│  └──────────────┘        └──────┬───────┘          │
│                                  │                   │
│  ┌──────────────┐               ▼                   │
│  │ Participant  │        ┌──────────────┐          │
│  │   (Bob)      │◀───────│   Action 2   │          │
│  └──────────────┘        │  "Approve"   │          │
│                          └──────────────┘          │
└─────────────────────────────────────────────────────┘
```

#### Implementation Plan
1. **Custom Node Types**:
   - `ParticipantNode`: Visual representation with wallet address, DID
   - `ActionNode`: Displays title, data requirements, routing logic
   - `DecisionNode`: Conditional routing visualization
   - `DataSchemaNode`: Expandable view of required fields

2. **Custom Ports**:
   - Input/output ports for action flow
   - Data ports for disclosure connections
   - Conditional ports for routing logic

3. **Right-Side Panel**:
   - Properties editor for selected node
   - Fluent API code preview
   - JSON preview
   - Validation errors

4. **Toolbar**:
   - Add participants, actions, decisions
   - Layout algorithms (auto-arrange)
   - Zoom controls
   - Export/Import
   - Validate blueprint

---

### Option 2: Excubo.Blazor.Diagrams (Lightweight Alternative)
**Library**: [Excubo.Blazor.Diagrams](https://www.nuget.org/packages/Excubo.Blazor.Diagrams)

#### Advantages
- ✅ **Open Source** (MIT License)
- ✅ **Lightweight** - Smaller footprint
- ✅ **Simple API** - Easier learning curve
- ✅ **Good Documentation** - Clear examples

#### Disadvantages
- ⚠️ **Less Feature-Rich** - Fewer built-in features
- ⚠️ **Less Active** - Slower update cycle
- ⚠️ **Limited Customization** - More constrained than Blazor.Diagrams

#### Best For
- Simpler workflows
- Faster initial development
- Learning/prototyping

---

### Option 3: Syncfusion Blazor Diagram (Commercial)
**Library**: [Syncfusion Blazor Diagram](https://www.syncfusion.com/blazor-components/blazor-diagram)

#### Advantages
- ✅ **Feature-Rich** - Extensive built-in features
- ✅ **Professional UI** - Polished components
- ✅ **BPMN Support** - Built-in BPMN notation
- ✅ **Mobile-Optimized** - Touch gestures built-in
- ✅ **Excellent Documentation** - Comprehensive guides

#### Disadvantages
- ❌ **Commercial License Required** - $995+/year (Community license available < $1M revenue)
- ❌ **Black Box** - Less control over internals
- ❌ **Large Bundle Size** - More overhead

#### Best For
- Enterprise deployments
- Budget available for licensing
- Need for professional support

---

### Option 4: Custom Canvas Solution
**Libraries**: Blazor.Extensions.Canvas + Custom Implementation

#### Advantages
- ✅ **Complete Control** - Every pixel under your control
- ✅ **Optimized** - Only what you need
- ✅ **No Dependencies** - Minimal external dependencies

#### Disadvantages
- ❌ **High Development Cost** - Build everything from scratch
- ❌ **More Bugs** - More code to maintain
- ❌ **Longer Timeline** - Significantly more development time

#### Best For
- Unique requirements
- Long-term investment
- Team with canvas/graphics expertise

---

## Recommended Approach: Option 1 (Blazor.Diagrams)

### Why Blazor.Diagrams?
1. **Open Source & Free** - No licensing constraints
2. **Blazor-Native** - Written for Blazor, not a JavaScript wrapper
3. **Highly Extensible** - Can build exactly what we need
4. **Active Community** - Good support and examples
5. **Performance** - Optimized for complex diagrams
6. **Mobile Support** - Touch events work well

---

## Application Architecture

### Component Structure
```
Sorcha.Blueprint.Designer (Blazor WASM)
│
├── Pages/
│   ├── Index.razor                    # Landing page
│   ├── Designer.razor                 # Main designer canvas
│   ├── Gallery.razor                  # Blueprint templates
│   └── Preview.razor                  # Workflow simulation
│
├── Components/
│   ├── Canvas/
│   │   ├── BlueprintCanvas.razor      # Main diagram canvas
│   │   ├── ParticipantNode.razor      # Custom participant node
│   │   ├── ActionNode.razor           # Custom action node
│   │   └── DecisionNode.razor         # Conditional routing node
│   │
│   ├── Panels/
│   │   ├── ToolboxPanel.razor         # Drag-and-drop toolbox
│   │   ├── PropertiesPanel.razor      # Node properties editor
│   │   ├── ValidationPanel.razor      # Validation results
│   │   └── CodePanel.razor            # C# Fluent API preview
│   │
│   ├── Editors/
│   │   ├── ParticipantEditor.razor    # Edit participant details
│   │   ├── ActionEditor.razor         # Edit action configuration
│   │   ├── DataSchemaEditor.razor     # JSON Schema builder
│   │   ├── DisclosureEditor.razor     # Data visibility rules
│   │   ├── ConditionEditor.razor      # JSON Logic visual editor
│   │   └── FormDesigner.razor         # UI form builder
│   │
│   └── Shared/
│       ├── Toolbar.razor              # Main toolbar
│       ├── JsonViewer.razor           # JSON preview
│       └── ValidationMessage.razor    # Error/warning display
│
├── Services/
│   ├── BlueprintService.cs            # Blueprint CRUD operations
│   ├── ValidationService.cs           # Fluent API validation
│   ├── ExportService.cs               # JSON/PNG export
│   ├── ImportService.cs               # JSON import
│   └── StorageService.cs              # LocalStorage/IndexedDB
│
├── Models/
│   ├── DiagramModels/
│   │   ├── ParticipantNodeModel.cs    # Node data models
│   │   ├── ActionNodeModel.cs
│   │   └── LinkModel.cs
│   │
│   └── ViewModels/
│       ├── DesignerViewModel.cs       # Designer state
│       └── PropertiesViewModel.cs     # Properties panel state
│
└── wwwroot/
    ├── css/
    │   ├── app.css                    # Global styles
    │   └── designer.css               # Canvas-specific styles
    │
    ├── js/
    │   └── interop.js                 # Minimal JS helpers
    │
    └── manifest.json                  # PWA manifest
```

---

## User Interface Design

### Layout (3-Panel Design)

```
┌─────────────────────────────────────────────────────────────────┐
│  [Sorcha] [File] [Edit] [View] [Validate] [Export]    [?] [☰]  │ Toolbar
├──────────┬──────────────────────────────────────────┬───────────┤
│          │                                          │           │
│ Toolbox  │         Canvas Area                     │Properties │
│          │                                          │           │
│ ┌──────┐ │  ┌─────────┐      ┌─────────┐          │ Selected: │
│ │  👤  │ │  │ Alice   │─────▶│Submit   │          │ Action 1  │
│ │Partcp│ │  │ Buyer   │      │Purchase │          │           │
│ └──────┘ │  └─────────┘      └────┬────┘          │ Title:    │
│          │                         │               │ [Submit ] │
│ ┌──────┐ │                         ▼               │           │
│ │  📋  │ │  ┌─────────┐      ┌─────────┐          │ Data:     │
│ │Action│ │  │ Bob     │◀─────│Approve  │          │ [Edit...] │
│ └──────┘ │  │Approver │      │Purchase │          │           │
│          │  └─────────┘      └─────────┘          │ Routing:  │
│ ┌──────┐ │                                         │ [Edit...] │
│ │  ◇   │ │                                         │           │
│ │Decisn│ │                                         │ [Validate]│
│ └──────┘ │                                         │           │
│          │                                         │ Errors: 0 │
│          │                                         │ Warnings:1│
└──────────┴──────────────────────────────────────────┴───────────┘
```

### Mobile/Tablet Layout (Adaptive)

```
┌─────────────────────────────┐
│ [☰] Sorcha    [Validate] [⋮]│ Compact Toolbar
├─────────────────────────────┤
│                             │
│     Canvas Area (Full)      │
│                             │
│  ┌─────────┐  ┌─────────┐  │
│  │ Alice   │─▶│Submit   │  │
│  │ Buyer   │  │Purchase │  │
│  └─────────┘  └────┬────┘  │
│                     │       │
│                     ▼       │
│  ┌─────────┐  ┌─────────┐  │
│  │ Bob     │◀─│Approve  │  │
│  │Approver │  │Purchase │  │
│  └─────────┘  └─────────┘  │
│                             │
│ [+] Add Node  [🔧]          │ Bottom Actions
└─────────────────────────────┘

// Tap node → Slide-up panel with properties
// Pinch to zoom
// Two-finger pan
// Long-press for context menu
```

---

## Key Features

### 1. Visual Node Types

#### Participant Node
```
┌──────────────────────┐
│ 👤 Alice (Buyer)     │
├──────────────────────┤
│ Org: ACME Corp       │
│ Wallet: 0x742d...    │
│ ⚫ Output Port       │
└──────────────────────┘
```

#### Action Node
```
┌──────────────────────┐
│ ⚫ Input              │
├──────────────────────┤
│ 📋 Submit Purchase   │
│ Sender: Alice        │
│ 📊 Data: 3 fields    │
│ 🔒 Disclosures: 2    │
├──────────────────────┤
│ ⚫ Output            │
└──────────────────────┘
```

#### Decision Node
```
       ┌───────┐
       │   ⚫   │ Input
       ├───────┤
       │   ◇   │
       │ Price │
       │ > 1000│
       ├───┬───┤
   Yes │   │   │ No
      ⚫   ⚫
```

### 2. Interactive Features

#### Canvas Interactions
- **Pan**: Click + drag background
- **Zoom**: Mouse wheel / pinch gesture
- **Select**: Click node
- **Multi-Select**: Ctrl/Cmd + click or drag selection box
- **Connect**: Drag from output port to input port
- **Delete**: Select + Delete/Backspace key
- **Copy/Paste**: Ctrl+C / Ctrl+V
- **Undo/Redo**: Ctrl+Z / Ctrl+Y

#### Node Interactions
- **Single Click**: Select and show properties
- **Double Click**: Open detailed editor
- **Drag**: Move node
- **Hover**: Show tooltip with details
- **Right Click**: Context menu (delete, duplicate, edit)

#### Mobile Touch Gestures
- **Tap**: Select node
- **Double Tap**: Edit node
- **Long Press**: Context menu
- **Pinch**: Zoom
- **Two-Finger Drag**: Pan canvas
- **Drag Node**: Move node

### 3. Toolbox Components

#### Draggable Items
- 👤 **Participant** - Add workflow participant
- 📋 **Action** - Add workflow step
- ◇ **Decision** - Add conditional routing
- 📊 **Data Schema** - Define data requirements
- 🔒 **Disclosure** - Configure data visibility
- 📝 **Form** - Design UI form

### 4. Properties Panel (Context-Sensitive)

#### When Participant Selected
```
┌─────────────────────────┐
│ Participant Properties  │
├─────────────────────────┤
│ ID: [alice________]     │
│ Name: [Alice______]     │
│ Org: [ACME Corp__]      │
│ Wallet: [0x742d...]     │
│ DID URI: [did:...  ]    │
│ □ Use Stealth Address  │
│                         │
│ [Save] [Cancel]         │
└─────────────────────────┘
```

#### When Action Selected
```
┌─────────────────────────┐
│ Action Properties       │
├─────────────────────────┤
│ Title: [Submit PO]      │
│ Desc: [__________]      │
│ Sender: [Alice ▼]       │
│                         │
│ ▶ Data Schema (3)       │
│ ▶ Disclosures (2)       │
│ ▶ Routing Logic         │
│ ▶ Calculations (1)      │
│ ▶ Form Design           │
│                         │
│ [Edit Details]          │
└─────────────────────────┘
```

### 5. Validation Panel
```
┌─────────────────────────┐
│ Validation Results      │
├─────────────────────────┤
│ ✅ 2 Participants       │
│ ✅ 3 Actions            │
│ ⚠️  Action 2: Missing   │
│    data schema          │
│ ❌ Action 3: Invalid    │
│    routing condition    │
│                         │
│ [Fix All] [Ignore]      │
└─────────────────────────┘
```

### 6. Code Preview Panel
```
┌─────────────────────────┐
│ Fluent API Preview      │
├─────────────────────────┤
│ var blueprint =         │
│   BlueprintBuilder      │
│     .Create()           │
│     .WithTitle("PO")    │
│     .AddParticipant(... │
│     .AddAction(...      │
│     .Build();           │
│                         │
│ [Copy Code]             │
└─────────────────────────┘
```

---

## Data Schema Visual Editor

### Field Type Palette
```
┌─────────────────────────────────────┐
│ Add Field:                          │
│ [📝 Text] [🔢 Number] [☑️ Boolean]   │
│ [📅 Date] [📁 File] [📦 Object]     │
│ [🔗 Array]                          │
└─────────────────────────────────────┘
```

### Schema Tree View
```
┌─────────────────────────────────────┐
│ Data Schema                         │
├─────────────────────────────────────┤
│ ▼ PurchaseOrder (object)            │
│   ├─ 📝 item (string) *required     │
│   │   ├─ Min: 3 chars               │
│   │   └─ Max: 200 chars             │
│   ├─ 🔢 quantity (integer) *req     │
│   │   └─ Min: 1                     │
│   ├─ 🔢 unitPrice (number) *req     │
│   │   └─ Min: 0                     │
│   └─ ▶ supplier (object)            │
│       ├─ 📝 name (string)           │
│       └─ 📝 address (string)        │
└─────────────────────────────────────┘
```

---

## JSON Logic Visual Editor

### Condition Builder
```
┌─────────────────────────────────────────────┐
│ Routing Condition                           │
├─────────────────────────────────────────────┤
│ IF [totalPrice▼] [>▼] [1000_______]        │
│ THEN route to: [senior-approver ▼]         │
│                                             │
│ ELSE route to: [approver ▼]                │
│                                             │
│ [+ Add Condition]                           │
│                                             │
│ Preview JSON Logic:                         │
│ {"if": [                                    │
│   {">": [{"var":"totalPrice"}, 1000]},      │
│   "senior-approver",                        │
│   "approver"                                │
│ ]}                                          │
└─────────────────────────────────────────────┘
```

### Calculation Builder
```
┌─────────────────────────────────────────────┐
│ Calculate: totalPrice                       │
├─────────────────────────────────────────────┤
│ [quantity▼] [×▼] [unitPrice▼]              │
│                                             │
│ Preview:                                    │
│ {"*": [                                     │
│   {"var": "quantity"},                      │
│   {"var": "unitPrice"}                      │
│ ]}                                          │
│                                             │
│ [Test with sample data]                     │
└─────────────────────────────────────────────┘
```

---

## Form Designer

### Visual Form Builder
```
┌──────────────────────────────────────┐
│ Form Designer                        │
├──────────────────────────────────────┤
│ Layout: [Vertical ▼]                 │
│                                      │
│ ┌────────────────────────────────┐  │
│ │ Item Description               │  │
│ │ [____________________]         │  │
│ │                                │  │
│ │ Quantity           Unit Price  │  │
│ │ [____]             [____]      │  │
│ │                                │  │
│ │ [Submit Purchase Order]        │  │
│ └────────────────────────────────┘  │
│                                      │
│ Controls:                            │
│ ▼ Item Description (TextLine)        │
│   ├─ Bound to: /item                 │
│   └─ Required: ✓                     │
│ ▼ Quantity (Numeric)                 │
│   ├─ Bound to: /quantity             │
│   └─ Required: ✓                     │
└──────────────────────────────────────┘
```

---

## Technology Stack

### Frontend
- **Blazor WebAssembly (.NET 10)** - Client-side execution
- **Blazor.Diagrams** - Diagram canvas library
- **MudBlazor** - UI component library (optional but recommended)
- **Blazored.LocalStorage** - Client-side storage
- **System.Text.Json** - JSON serialization

### Development Tools
- **Hot Reload** - Fast development iteration
- **Browser DevTools** - Debugging
- **Lighthouse** - Performance auditing

### Build & Deployment
- **AOT Compilation** - Faster runtime performance
- **Trimming** - Smaller bundle size
- **Compression** - Brotli/Gzip
- **CDN** - Static file hosting (GitHub Pages, Azure Static Web Apps)

---

## Implementation Phases

### Phase 1: Foundation (Week 1-2)
- ✅ Set up Blazor WASM project
- ✅ Integrate Blazor.Diagrams
- ✅ Create basic 3-panel layout
- ✅ Implement participant and action nodes
- ✅ Basic drag-and-drop from toolbox
- ✅ Node selection and properties panel

### Phase 2: Core Features (Week 3-4)
- ✅ Properties editor for participants
- ✅ Properties editor for actions
- ✅ Data schema visual editor
- ✅ Disclosure configuration
- ✅ Basic validation
- ✅ JSON import/export

### Phase 3: Advanced Features (Week 5-6)
- ✅ JSON Logic visual editor
- ✅ Conditional routing UI
- ✅ Calculation builder
- ✅ Form designer
- ✅ Full Fluent API integration
- ✅ Comprehensive validation

### Phase 4: Polish & Testing (Week 7-8)
- ✅ Mobile/tablet optimization
- ✅ Touch gesture support
- ✅ Undo/redo functionality
- ✅ Auto-save to local storage
- ✅ Blueprint templates/examples
- ✅ User documentation
- ✅ Performance optimization

### Phase 5: PWA & Enhancement (Week 9-10)
- ✅ PWA configuration
- ✅ Offline support
- ✅ Install prompts
- ✅ Blueprint gallery
- ✅ Workflow simulation/preview
- ✅ Export to PNG/SVG

---

## Performance Considerations

### Bundle Size Optimization
- **Lazy Loading**: Load diagram library only on designer page
- **Code Splitting**: Separate routes into chunks
- **Tree Shaking**: Remove unused code
- **AOT Compilation**: Ahead-of-time compilation for faster startup

### Runtime Performance
- **Virtualization**: Only render visible nodes
- **Debouncing**: Throttle validation during editing
- **Web Workers**: Offload validation to background thread
- **Memoization**: Cache computed properties

### Mobile Optimization
- **Touch Targets**: Minimum 44x44px tap areas
- **Viewport**: Proper meta viewport configuration
- **Font Loading**: Optimize web font loading
- **Image Optimization**: Use WebP format for icons

---

## Alternative UI Libraries (Optional)

### MudBlazor (Recommended)
- Material Design components
- Excellent mobile support
- Rich form controls
- Dialogs, drawers, snackbars
- Theme customization

### Radzen
- Professional component library
- Good DataGrid support
- Form validation
- Free community edition

### Ant Design Blazor
- Enterprise-grade UI
- Comprehensive components
- Good i18n support

---

## Next Steps

1. **Decision**: Confirm Blazor.Diagrams as the diagram library
2. **UI Framework**: Choose MudBlazor vs vanilla CSS
3. **Start Phase 1**: Set up project structure and basic canvas
4. **Prototype**: Create proof-of-concept with 2 nodes and 1 connection
5. **Iterate**: Gather feedback and refine UX

---

## Open Questions

1. **Offline Storage**: Use LocalStorage or IndexedDB for blueprints?
2. **Collaboration**: Future multi-user editing (operational transforms)?
3. **Versioning**: Built-in blueprint version control?
4. **Templates**: Pre-built blueprint templates for common workflows?
5. **Integration**: API for programmatic blueprint creation from external systems?
6. **Testing**: Visual regression testing for diagram layout?
