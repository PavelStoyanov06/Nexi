# 1. UI Layer
- Nexi.UI - contains the UI logi
- Nexi.Desktop - contains configurations for launching Nexi on the desktop.
# 2. Services Layer
- Nexi.Services - contains the core application logic
<br>
<br> ⚠️ **This layer is subject to change due to the complexity of the solution. The project can be fragmented into smaller class libraries, for instance: Nexi.Services.AI, Nexi.Services.Speech, etc.**
# 3. Data Layer
- Nexi.Data - contains core data logi, meaning the database context is stored here, if needed
- Nexi.Data.Models - contains all entity models, meaning models for things like user account and other creatables throughout the project
# 4. Tests Layer
- Nexi.Services.Tests - contains unit tests for the Nexi.Services project
- Nexi.UI.Tests - contains unit tests for the Nex.UI project
<br>
<br> ⚠️ **This layer is also subject to change due to possible modifications in other layers, for instance, if a new project that needs to be tested is added, or another project gets fragmented into smaller ones.**

**All projects listed in this markdown are subject to change. These are just the core projects created in order to establish the basic project logic.**
