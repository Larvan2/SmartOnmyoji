import { ChevronDownIcon } from "lucide-react"
import {
  Controller,
  type FieldPathByValue,
  type UseFormReturn,
} from "react-hook-form"

import type { Options } from "@/api/schemas"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"
import { Separator } from "@/components/ui/separator"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"

type Form = UseFormReturn<Options>

interface Props {
  form: Form
  /** Base UI controls do not inherit `disabled` from an enclosing fieldset. */
  disabled: boolean
}

export function OptionsForm({ form, disabled }: Props) {
  const mode = form.watch("mode")

  return (
    <FieldGroup className="gap-4">
      <FieldGroup className="grid grid-cols-2 gap-3">
        <Field className="col-span-2">
          <FieldLabel>模式</FieldLabel>
          <Controller
            control={form.control}
            name="mode"
            render={({ field }) => (
              <ToggleGroup
                variant="outline"
                spacing={0}
                disabled={disabled}
                value={[field.value]}
                onValueChange={(v) => {
                  // clicking the pressed item would clear the group; a mode is always set
                  if (v[0]) field.onChange(v[0])
                }}
              >
                <ToggleGroupItem value="ByRounds">按回合</ToggleGroupItem>
                <ToggleGroupItem value="ByMinutes">按时长</ToggleGroupItem>
              </ToggleGroup>
            )}
          />
        </Field>

        {/* Keyed: without distinct keys React would reuse one <input> for both
            fields and the two registrations would overwrite each other's value. */}
        {mode === "ByRounds" ? (
          <NumberField
            key="rounds"
            form={form}
            name="rounds"
            label="回合数"
            min={1}
            step={1}
          />
        ) : (
          <NumberField
            key="durationMinutes"
            form={form}
            name="durationMinutes"
            label="时长(分钟)"
            min={1}
            step={1}
          />
        )}
        <Field>
          <FieldLabel htmlFor="priority">进程优先级</FieldLabel>
          <Controller
            control={form.control}
            name="priority"
            render={({ field }) => (
              <NativeSelect
                id="priority"
                className="w-full"
                value={field.value ?? ""}
                onChange={(e) => field.onChange(e.target.value || null)}
              >
                <NativeSelectOption value="">不修改</NativeSelectOption>
                <NativeSelectOption value="Normal">Normal</NativeSelectOption>
                <NativeSelectOption value="AboveNormal">
                  AboveNormal
                </NativeSelectOption>
                <NativeSelectOption value="High">High</NativeSelectOption>
              </NativeSelect>
            )}
          />
        </Field>

        <CheckField
          form={form}
          name="perWindowRounds"
          label="多窗口独立计回合"
          disabled={disabled}
          className="col-span-2"
        />

        <RangeField
          form={form}
          label="匹配间隔(秒)"
          minName="intervalMin"
          maxName="intervalMax"
          step={0.1}
        />
        <NumberField
          form={form}
          name="matchThreshold"
          label="匹配阈值"
          min={0}
          max={1}
          step={0.01}
        />
      </FieldGroup>

      <Separator />

      <Collapsible defaultOpen>
        <CollapsibleTrigger className="group flex w-full items-center justify-between text-sm font-medium">
          防检测
          <ChevronDownIcon className="size-4 text-muted-foreground transition-transform group-data-panel-open:rotate-180" />
        </CollapsibleTrigger>
        <CollapsibleContent>
          <FieldGroup className="gap-4 pt-3">
            <FieldGroup className="grid grid-cols-2 gap-3">
              <CheckField
                form={form}
                name="antiDetection.enabled"
                label="启用防检测"
                disabled={disabled}
                className="col-span-2"
              />
              <NumberField
                form={form}
                name="antiDetection.clickDeviation"
                label="点击偏移 σ(px)"
                min={0}
                step={1}
              />
              <NumberField
                form={form}
                name="antiDetection.repeatSameTargetStop"
                label="卡死保护 N 次"
                min={2}
                step={1}
              />
              <CheckField
                form={form}
                name="antiDetection.enableRepeatStop"
                label="连续同图停"
                disabled={disabled}
                className="col-span-2"
              />
            </FieldGroup>

            <FieldSet className="gap-3">
              <FieldLegend variant="label">概率随机等待</FieldLegend>
              <FieldGroup className="grid grid-cols-2 gap-3">
                <CheckField
                  form={form}
                  name="antiDetection.randomWait.enabled"
                  label="启用"
                  disabled={disabled}
                  className="col-span-2"
                />
                <NumberField
                  form={form}
                  name="antiDetection.randomWait.probability"
                  label="概率"
                  min={0}
                  max={1}
                  step={0.01}
                />
                <NumberField
                  form={form}
                  name="antiDetection.randomWait.minGapSeconds"
                  label="最小间隔(秒)"
                  min={0}
                  step={1}
                />
                <RangeField
                  form={form}
                  label="等待秒范围"
                  minName="antiDetection.randomWait.minSeconds"
                  maxName="antiDetection.randomWait.maxSeconds"
                  step={1}
                />
              </FieldGroup>
            </FieldSet>

            <FieldSet className="gap-3">
              <FieldLegend variant="label">时间窗口频次上限</FieldLegend>
              <FieldGroup className="grid grid-cols-2 gap-3">
                <CheckField
                  form={form}
                  name="antiDetection.frequencyCap.enabled"
                  label="启用"
                  disabled={disabled}
                  className="col-span-2"
                />
                <NumberField
                  form={form}
                  name="antiDetection.frequencyCap.windowMinutes"
                  label="窗口(分)"
                  min={1}
                  step={1}
                />
                <NumberField
                  form={form}
                  name="antiDetection.frequencyCap.maxRounds"
                  label="上限局数"
                  min={1}
                  step={1}
                />
                <NumberField
                  form={form}
                  name="antiDetection.frequencyCap.extraWaitSeconds"
                  label="额外等待(秒)"
                  min={0}
                  step={1}
                />
              </FieldGroup>
            </FieldSet>
          </FieldGroup>
        </CollapsibleContent>
      </Collapsible>
    </FieldGroup>
  )
}

type NumberPath = FieldPathByValue<Options, number>

interface NumberProps {
  min?: number
  max?: number
  step: number
}

function NumberField({
  form,
  name,
  label,
  ...range
}: NumberProps & { form: Form; name: NumberPath; label: string }) {
  const error = form.getFieldState(name, form.formState).error
  return (
    <Field data-invalid={error !== undefined}>
      <FieldLabel htmlFor={name}>{label}</FieldLabel>
      <Input
        id={name}
        type="number"
        aria-invalid={error !== undefined}
        {...range}
        {...form.register(name, { valueAsNumber: true })}
      />
      <FieldError errors={[error]} />
    </Field>
  )
}

function RangeField({
  form,
  label,
  minName,
  maxName,
  step,
}: {
  form: Form
  label: string
  minName: NumberPath
  maxName: NumberPath
  step: number
}) {
  const errors = [minName, maxName].map(
    (n) => form.getFieldState(n, form.formState).error
  )
  const invalid = errors.some((e) => e !== undefined)
  return (
    <Field data-invalid={invalid} className="col-span-2">
      <FieldLabel htmlFor={minName}>{label}</FieldLabel>
      <div className="flex items-center gap-2">
        <Input
          id={minName}
          type="number"
          min={0}
          step={step}
          aria-invalid={errors[0] !== undefined}
          {...form.register(minName, { valueAsNumber: true })}
        />
        <span className="text-muted-foreground">~</span>
        <Input
          type="number"
          min={0}
          step={step}
          aria-label={`${label}上限`}
          aria-invalid={errors[1] !== undefined}
          {...form.register(maxName, { valueAsNumber: true })}
        />
      </div>
      <FieldError errors={errors} />
    </Field>
  )
}

function CheckField({
  form,
  name,
  label,
  disabled,
  className,
}: {
  form: Form
  name: FieldPathByValue<Options, boolean>
  label: string
  disabled: boolean
  className?: string
}) {
  return (
    <Field orientation="horizontal" className={className}>
      <Label className="font-normal">
        <Controller
          control={form.control}
          name={name}
          render={({ field }) => (
            <Checkbox
              checked={field.value}
              onCheckedChange={field.onChange}
              disabled={disabled}
            />
          )}
        />
        {label}
      </Label>
    </Field>
  )
}
