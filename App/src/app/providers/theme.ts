import {
  createLightTheme,
  type BrandVariants,
  type Theme,
} from '@fluentui/react-components'

// The HealthyHires theme (ADR-F009), from the design system v1.0 template. Fluent derives its brand tokens from
// one ramp; 80 is the primary orange, lower steps are darker and higher ones lighter.
const brand: BrandVariants = {
  10: '#371706',
  20: '#52240A',
  30: '#6D300E',
  40: '#883D12',
  50: '#A24A15',
  60: '#BD5619',
  70: '#D8631D',
  80: '#F37021',
  90: '#F4803A',
  100: '#F69052',
  110: '#F7A06B',
  120: '#F8B084',
  130: '#FABF9C',
  140: '#FBCFB5',
  150: '#FCDFCE',
  160: '#FEEFE6',
}

export const lightTheme: Theme = {
  ...createLightTheme(brand),

  // Pressed is one step darker than hover, as in the template, not Fluent's default of four.
  colorBrandBackgroundHover: brand[70],
  colorBrandBackgroundPressed: brand[60],

  // Neutrals (template: "Neutrálne farby").
  colorNeutralForeground1: '#1F2933',
  colorNeutralForeground2: '#374151',
  colorNeutralForeground3: '#687280',
  colorNeutralStroke1: '#9CA3AF',
  colorNeutralStroke2: '#E5E7EB',
  colorNeutralBackground1: '#FFFFFF',
  colorNeutralBackground2: '#F7F8FA',
  colorNeutralBackground3: '#F3F4F6',

  // Inter, self-hosted: the CSP allows fonts only from the app's own origin.
  fontFamilyBase: "'Inter Variable', 'Segoe UI', system-ui, sans-serif",

  // Radii 4 / 6 / 8 / 12 and the Low / Medium / High shadows.
  borderRadiusSmall: '4px',
  borderRadiusMedium: '6px',
  borderRadiusLarge: '8px',
  borderRadiusXLarge: '12px',
  shadow2: '0 1px 2px 0 rgba(0, 0, 0, 0.05)',
  shadow4: '0 1px 2px 0 rgba(0, 0, 0, 0.05)',
  shadow8: '0 4px 8px 0 rgba(0, 0, 0, 0.08)',
  shadow16: '0 8px 16px 0 rgba(0, 0, 0, 0.12)',
}
