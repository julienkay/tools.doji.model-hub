# com.doji.hf-hub

Reference and download AI models from HuggingFace for Unity packages.

Shared Hub logic should live in `Runtime` whenever possible so same package can later support downloading files from HuggingFace at runtime. Keep only UnityEditor-specific integration, Package Manager scanning, prompts, and asset import workflows in `Editor`.
