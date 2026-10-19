/**
 * The app's icons: Tabler, 1.5 px stroke by default, exported under the names the app has always used
 * (they came from Lucide). Import icons only from here.
 */
// Static icon module: exports are factory-made components, so fast refresh of this file does not matter.
/* oxlint-disable react/only-export-components */
import {
  IconAdjustmentsHorizontal,
  IconAlertCircle,
  IconAlertOctagon,
  IconAlertTriangle,
  IconArrowsMaximize,
  IconArrowsMinimize,
  IconCheck,
  IconChevronLeft,
  IconChevronRight,
  IconCircleCheck,
  IconCircleDashed,
  IconCircleX,
  IconClock,
  IconCloudUpload,
  IconCopy,
  IconCrosshair,
  IconDeviceFloppy,
  IconDots,
  IconExternalLink,
  IconEye,
  IconEyeOff,
  IconFlame,
  IconFolderOpen,
  IconInfoCircle,
  IconKey,
  IconLayoutDashboard,
  IconLayoutGrid,
  IconLoader2,
  IconLogin,
  IconLogout,
  IconMaximize,
  IconPencil,
  IconPictureInPicture,
  IconPlayerPause,
  IconPlayerPlay,
  IconPlayerSkipBack,
  IconPlayerSkipForward,
  IconPlus,
  IconRadar,
  IconRotate,
  IconSearch,
  IconSettings,
  IconShieldCheck,
  IconSparkles,
  IconTrash,
  IconUserPlus,
  IconUsers,
  IconVolume,
  IconVolumeOff,
  IconX,
  type Icon as TablerIcon,
  type IconProps,
} from '@tabler/icons-react'
import type { ReactElement } from 'react'

export type AppIcon = (props: IconProps) => ReactElement

function icon(Base: TablerIcon): AppIcon {
  const Wrapped = (props: IconProps) => <Base stroke={1.5} {...props} />
  Wrapped.displayName = Base.displayName
  return Wrapped
}

export const AlertCircle = icon(IconAlertCircle)
export const AlertTriangle = icon(IconAlertTriangle)
export const TriangleAlert = AlertTriangle
export const Check = icon(IconCheck)
export const CheckCircle2 = icon(IconCircleCheck)
export const ChevronLeft = icon(IconChevronLeft)
export const ChevronRight = icon(IconChevronRight)
export const CircleDashed = icon(IconCircleDashed)
export const Clock = icon(IconClock)
export const Copy = icon(IconCopy)
export const Crosshair = icon(IconCrosshair)
export const ExternalLink = icon(IconExternalLink)
export const Eye = icon(IconEye)
export const EyeOff = icon(IconEyeOff)
export const Flame = icon(IconFlame)
export const FolderOpen = icon(IconFolderOpen)
export const Info = icon(IconInfoCircle)
export const KeyRound = icon(IconKey)
export const LayoutTemplate = icon(IconLayoutDashboard)
export const Loader2 = icon(IconLoader2)
export const LogIn = icon(IconLogin)
export const LogOut = icon(IconLogout)
export const Maximize = icon(IconMaximize)
export const Maximize2 = icon(IconArrowsMaximize)
export const Minimize2 = icon(IconArrowsMinimize)
export const MoreHorizontal = icon(IconDots)
export const OctagonAlert = icon(IconAlertOctagon)
export const PanelsTopLeft = icon(IconLayoutGrid)
export const Pause = icon(IconPlayerPause)
export const Pencil = icon(IconPencil)
export const PictureInPicture2 = icon(IconPictureInPicture)
export const Play = icon(IconPlayerPlay)
export const Plus = icon(IconPlus)
export const Radar = icon(IconRadar)
export const RotateCcw = icon(IconRotate)
export const Save = icon(IconDeviceFloppy)
export const Search = icon(IconSearch)
export const Settings2 = icon(IconSettings)
export const ShieldCheck = icon(IconShieldCheck)
export const SkipBack = icon(IconPlayerSkipBack)
export const SkipForward = icon(IconPlayerSkipForward)
export const SlidersHorizontal = icon(IconAdjustmentsHorizontal)
export const Sparkles = icon(IconSparkles)
export const Trash2 = icon(IconTrash)
export const UploadCloud = icon(IconCloudUpload)
export const UserPlus = icon(IconUserPlus)
export const Users = icon(IconUsers)
export const Volume2 = icon(IconVolume)
export const VolumeX = icon(IconVolumeOff)
export const X = icon(IconX)
export const XCircle = icon(IconCircleX)
