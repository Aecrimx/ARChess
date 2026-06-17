# Color Theme

The project currently mixes a dark menu theme, warm HUD text, and high-contrast board highlights. Values below are taken from the serialized/script defaults.

## Main Menu

| Purpose | Hex | Source |
| --- | --- | --- |
| Background | `#14141F` | `MainMenuController.backgroundColor` |
| Button | `#33334D` | `MainMenuController.buttonColor` |
| Button hover | `#4D4D73` | `MainMenuController.buttonHoverColor` |
| Title/text | `#FFFFFF` | `MainMenuController.titleColor` and `buttonTextColor` |

## In-Game Menu And HUD

| Purpose | Hex | Alpha | Source |
| --- | --- | --- | --- |
| Dialog overlay | `#000000` | 72% | `GameplayHUDController.menuOverlayColor` |
| Panel | `#2E243D` | 100% | `GameplayHUDController.menuPanelColor` |
| Primary button | `#967AB5` | 100% | `GameplayHUDController.menuPrimaryButtonColor` |
| Secondary button | `#524766` | 100% | `GameplayHUDController.menuSecondaryButtonColor` |
| Menu text | `#FAF0E6` | 100% | `GameplayHUDController.menuTextColor` |
| AI coach bubble | `#14141A` | 88% | `GameplayHUDController.BuildAiCoachBubble` |

## Board Highlights

The same default palette is used by `Chess2DInputHandler` and `ChessARInputHandler`.

| Purpose | Hex | Alpha |
| --- | --- | --- |
| Selected square | `#33D933` | 60% |
| Legal move | `#3399FF` | 50% |
| Last move | `#FFD900` | 40% |
| Check | `#FF1A1A` | 55% |

## AR Runtime Visuals

| Purpose | Hex | Alpha | Source |
| --- | --- | --- | --- |
| AR plane visualization | `#26B3E6` | 15% | `ChessViewModeController.CreatePlaneTemplate` |
| AR placement indicator | `#26D9F2` | 55% | `ChessARInputHandler.EnsurePlacementIndicator` |

## Legacy Palette

The older project notes used these values:

| Purpose | Hex |
| --- | --- |
| Opponent color | `#967BB6` |
| Player color | `#FAF0E6` |
| Opponent highlight | `#5D4A75` |
| Player highlight | `#7A6F66` |

These legacy values now mostly survive through the in-game primary button and warm text colors. The actual board highlighting palette is the green/blue/yellow/red set listed above.
