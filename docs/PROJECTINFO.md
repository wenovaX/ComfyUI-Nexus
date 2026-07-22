# Project Info

[한국어](PROJECTINFO.ko.md)

## Product Name

- Official product name: **Nexus for ComfyUI**
- Short brand name: **Nexus**
- Internal/project alias: **ComfyUI Nexus**
- Repository name: **ComfyUI-Nexus**

Use **Nexus for ComfyUI** for public-facing store listings, screenshots, release notes, and user documentation.<br>
Use **ComfyUI Nexus** only as an internal or informal project alias where the context is clearly this repository.

## Positioning

Nexus for ComfyUI is an independent desktop companion for ComfyUI.

It helps users install, launch, monitor, repair, and maintain a local ComfyUI workspace on Windows.<br>
The app is a companion and runtime manager, not an official ComfyUI distribution.

## Public Description

Recommended short description:

> An independent desktop companion for ComfyUI.

Recommended longer description:

> Install, launch, monitor, and maintain a local ComfyUI workspace on Windows with a guided setup flow, managed extensions, external model library support, and server diagnostics.

## Store And Branding Rules

- Do not present the app as an official ComfyUI product.
- Prefer **Nexus for ComfyUI** over **ComfyUI Nexus** in store-facing copy.
- Keep the repository and technical identifiers as **ComfyUI-Nexus** unless a later packaging pass explicitly renames them.
- Mention that the current release targets Windows with NVIDIA CUDA GPUs.
- Avoid generic names such as "Stable Diffusion Generator" that imply the app is a standalone image generation service.

## Runtime Ownership

- Vanguard mode manages a Nexus-owned local ComfyUI runtime.
- Architect mode attaches to an existing user-managed ComfyUI workspace.
- User data such as models, workflows, inputs, outputs, custom nodes,<br>
and external model paths must be treated as runtime data and preserved during repair or migration flows.

## Store Packaging Notes

Microsoft Store packaging may require a different writable runtime root than the portable release.

The portable release can keep using the app folder as the runtime root.<br>
Store/MSIX builds should use a user-writable application data location or an explicitly selected workspace folder,<br>
because the installed package location is not a general writable runtime directory.
