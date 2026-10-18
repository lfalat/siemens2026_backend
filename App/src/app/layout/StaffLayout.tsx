import { useMsal } from '@azure/msal-react'
import { Avatar, Button, Text } from '@fluentui/react-components'
import { SignOutRegular } from '@fluentui/react-icons'
import { AppShell } from './AppShell.tsx'

export function StaffLayout() {
  const { instance } = useMsal()
  const account = instance.getActiveAccount()
  const name = account?.name ?? account?.username

  return (
    <AppShell
      homeTo="/staff"
      actions={
        <>
          <Avatar name={name} color="neutral" />
          <Text>{name}</Text>
          <Button
            icon={<SignOutRegular />}
            // Clears the tokens in this tab and ends the Entra session in this browser (ADR-F006).
            onClick={() => void instance.logoutRedirect({ account })}
          >
            Sign out
          </Button>
        </>
      }
    />
  )
}
