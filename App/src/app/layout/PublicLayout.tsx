import { Button } from '@fluentui/react-components'
import { PersonRegular } from '@fluentui/react-icons'
import { useNavigate } from 'react-router'
import { AppShell } from './AppShell.tsx'

export function PublicLayout() {
  const navigate = useNavigate()

  return (
    <AppShell
      homeTo="/"
      actions={
        // A link styled as a button: it navigates to the staff area, which starts the Entra sign-in.
        <Button
          as="a"
          href="/staff"
          appearance="primary"
          icon={<PersonRegular />}
          onClick={(event) => {
            event.preventDefault()
            void navigate('/staff')
          }}
        >
          Staff sign in
        </Button>
      }
    />
  )
}
