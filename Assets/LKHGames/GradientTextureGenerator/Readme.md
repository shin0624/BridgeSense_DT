Copyright� LKHGames

Feel free to use the tool on your project, it is free for all.

Warning!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
ToDo before update.
Move all the generated texture away from my default saving path.
Delete old file before updating.

# Workflow A: Baking Textures to Disk
1. Add the `GradientGenerator` script to any GameObject.
2. Configure the **gradient**.
3. (Optional) Set **savingPath** if you use a custom output folder.
4. Set **width**, **height**, and **rotate90** as needed.
5. Choose **textureFormat**.
6. Click **"Generate Gradient Texture"** in the Inspector.

Baked files are saved as `GradientTexture_<random>.<ext>` in `Assets` + `savingPath`.

# Workflow B: Live Gradient on Material (Play Mode)
1. In your shader, expose a 2D texture property and note its **Reference** name.
2. In the Gradient Generator, enter that name in **propertiesName**.
3. Assign the **materialRenderer** (Renderer on the object using the material).
4. Set **onPlayMode**:
   - **UpdateOnStart** — Gradient applied once when Play starts.
   - **UpdateEveryFrame** — Gradient updated every frame (for animation).

Full Documentation https://lkhgames.gitbook.io/lkhgames-assets-documentation

Enjoy & Good luck!
Copyright� LKHGames