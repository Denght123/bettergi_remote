# BetterGI Remote · Light UI design contract

2026-10-04. User rejected the dark green theme, native system frames, default CJK typography, verbose help, misalignment and the ambiguous waveform logo. This contract replaces those decisions.

## Visual authority
The user-supplied BetterGI 0.65.0 screenshot is the reference for a calm, entirely light application shell, clear functional hierarchy and unobtrusive window controls. Do not restore the rejected dark theme.

## Audit findings and corrective rules
- Actual bundled CJK typeface: Remote UI Sans (renamed Noto Sans SC subset), regular 400 and semibold 600; Manrope for the wordmark/Latin on the web. No system display font as the intentional typeface.
- Type tokens: caption 12px, body/control 14px, section 16px semibold, screen title 28px semibold. Native points = logical pixels × 72/96; use the same measured font metrics for layout and painting.
- Grid: 4px micro rhythm; group gaps 16/24px; section padding 24px. Input and button content must be centered vertically from PreferredHeight/text measurement, not fixed y offsets.
- Color: paper #F4F6F9; surface #FFFFFF; ink #2C3540; secondary #626F7E; restrained blue action color. All backgrounds remain light. Text contrast is verified, including hints and placeholders.
- One functional heading per section. Remove duplicated versions, slogans and always-visible paragraphs. Explanations live in accessible on-demand help; risk/quantity/security notices remain visible when relevant.
- Mark: recognizable linked desktop + phone; no waveform/egg, rotating compass or arbitrary geometry.
- Windows: client-drawn caption, native minimize/maximize/close semantics and resize edges; preserve OS file pickers and UAC.
- Motion: brief state/route feedback, no idle ornament. Reduced-motion preference respected. Never animate a status redraw or block editing.

## Scope and evidence
Backend source, runtime, crypto, storage, protocol and RPC logic remain frozen. UI preview uses an isolated profile and simulated data, with real-save disabled. Tests and desktop/browser captures, not a polished mockup, are delivery evidence.

## Desktop-only refinement (user approved mobile)
Read BetterGI App.xaml/MainWindow.xaml/HomePage.xaml at cached upstream revision 97c90aa: shared MiSans-Regular typography, WPF UI SymbolIcon and restrained 6/8px-radius controls. Implemented native regular 400 controls, medium 500 headings, canonical Fluent Regular functional icons at a fixed square aspect ratio, softened low-contrast hairlines and quiet disabled borders. Preserve the already-reviewed PC/phone brand mark. Do not edit mobile files in this round.
