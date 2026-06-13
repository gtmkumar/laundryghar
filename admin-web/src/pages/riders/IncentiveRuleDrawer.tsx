import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Gift } from 'lucide-react'
import { useCreateIncentiveRule, useUpdateIncentiveRule } from '@/hooks/useIncentives'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { FieldError } from '@/components/ui/FieldError'
import { positiveMoney, nonNegativeInt } from '@/lib/validation'
import type { IncentiveRuleDto, IncentiveRuleType } from '@/types/api'

const RULE_TYPES: { value: IncentiveRuleType; label: string; help: string }[] = [
  {
    value: 'trips_target',
    label: 'Trips target',
    help: 'Award the reward once a rider completes this many deliveries in a day.',
  },
  {
    value: 'surge_bonus',
    label: 'Surge bonus',
    help: 'Award the reward per delivery completed during a fare surge window.',
  },
]

const schema = z
  .object({
    name: z.string().trim().min(1, 'Give the rule a name.').max(120, 'Keep it under 120 characters.'),
    ruleType: z.enum(['trips_target', 'surge_bonus'] as const),
    // Threshold only matters for trips_target; surge_bonus ignores it server-side.
    threshold: nonNegativeInt,
    rewardAmount: positiveMoney,
    isActive: z.boolean(),
    validUntil: z.string(),
  })
  .refine((v) => v.ruleType !== 'trips_target' || v.threshold >= 1, {
    path: ['threshold'],
    message: 'Trips target needs a threshold of at least 1.',
  })

type FormValues = z.infer<typeof schema>

const emptyValues: FormValues = {
  name: '',
  ruleType: 'trips_target',
  threshold: 1,
  rewardAmount: 0,
  isActive: true,
  validUntil: '',
}

export function IncentiveRuleDrawer({
  rule,
  open,
  onClose,
}: {
  /** Null → create mode; a rule → edit mode. */
  rule: IncentiveRuleDto | null
  open: boolean
  onClose: () => void
}) {
  const isEdit = rule !== null
  const create = useCreateIncentiveRule()
  const update = useUpdateIncentiveRule()

  const {
    register,
    handleSubmit,
    reset,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: emptyValues,
  })

  // Reset to the edited rule (or blank) whenever the drawer opens.
  useEffect(() => {
    if (!open) return
    reset(
      rule
        ? {
            name: rule.name,
            ruleType: rule.ruleType,
            threshold: rule.threshold,
            rewardAmount: rule.rewardAmount,
            isActive: rule.isActive,
            validUntil: rule.validUntil ? rule.validUntil.slice(0, 10) : '',
          }
        : emptyValues,
    )
  }, [open, rule, reset])

  const ruleType = watch('ruleType')
  const showThreshold = ruleType === 'trips_target'

  const submit = handleSubmit(async (values) => {
    const payload = {
      name: values.name.trim(),
      ruleType: values.ruleType,
      // surge_bonus ignores threshold; send 0 so we never carry a stale value.
      threshold: values.ruleType === 'trips_target' ? values.threshold : 0,
      rewardAmount: values.rewardAmount,
      isActive: values.isActive,
      validUntil: values.validUntil ? values.validUntil : null,
    }
    if (rule) await update.mutateAsync({ id: rule.id, payload })
    else await create.mutateAsync(payload)
    onClose()
  })

  if (!open) return null

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Gift}
      eyebrow="Rider incentives"
      title={isEdit ? 'Edit incentive rule' : 'New incentive rule'}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? 'Save changes' : 'Create rule'}
      submittingLabel="Saving…"
      submitting={isSubmitting || create.isPending || update.isPending}
    >
      <DrawerSection>
        <Field label="Name" name="name">
          <input
            {...register('name')}
            className={drawerInputCls}
            placeholder="e.g. 10 trips a day bonus"
            autoFocus
          />
          <FieldError message={errors.name?.message} />
        </Field>

        <Field
          label="Rule type"
          name="ruleType"
          hint={RULE_TYPES.find((t) => t.value === ruleType)?.help}
        >
          <select {...register('ruleType')} className={drawerInputCls}>
            {RULE_TYPES.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
          <FieldError message={errors.ruleType?.message} />
        </Field>

        {showThreshold && (
          <Field label="Threshold (deliveries / day)" name="threshold">
            <input
              type="number"
              min={1}
              step={1}
              {...register('threshold', { valueAsNumber: true })}
              className={drawerInputCls}
              placeholder="e.g. 10"
            />
            <FieldError message={errors.threshold?.message} />
          </Field>
        )}

        <Field label="Reward amount (₹)" name="rewardAmount">
          <input
            type="number"
            min={0}
            step="0.01"
            {...register('rewardAmount', { valueAsNumber: true })}
            className={drawerInputCls}
            placeholder="e.g. 150"
          />
          <FieldError message={errors.rewardAmount?.message} />
        </Field>

        <Field label="Valid until" name="validUntil" hint="Leave blank for no end date.">
          <input type="date" {...register('validUntil')} className={drawerInputCls} />
          <FieldError message={errors.validUntil?.message} />
        </Field>

        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input
            type="checkbox"
            {...register('isActive')}
            className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green"
          />
          Active
        </label>
      </DrawerSection>
    </FormDrawer>
  )
}
