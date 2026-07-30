-- GitHub風の "> [!NOTE]" / "> [!IMPORTANT]" ブロッククォートを、
-- manual-style.cssのcalloutスタイルに合わせたDivへ変換するpandoc Luaフィルタ。

local function markerKind(text)
  if text == "[!NOTE]" then
    return "note"
  elseif text == "[!IMPORTANT]" then
    return "important"
  end
  return nil
end

function BlockQuote(el)
  if #el.content == 0 then return el end

  local first = el.content[1]
  if first.t ~= "Para" and first.t ~= "Plain" then return el end
  if #first.content == 0 then return el end

  local marker = first.content[1]
  if marker.t ~= "Str" then return el end

  local kind = markerKind(marker.text)
  if not kind then return el end

  -- マーカー行の直後の改行を1つだけ読み飛ばし、残りを本文として扱う
  local remaining = {}
  local skippedBreak = false
  for i = 2, #first.content do
    local inline = first.content[i]
    if not skippedBreak and (inline.t == "SoftBreak" or inline.t == "LineBreak") then
      skippedBreak = true
    else
      table.insert(remaining, inline)
    end
  end

  local title = kind == "important" and "重要" or "補足"
  local titlePara = pandoc.Para({ pandoc.Strong({ pandoc.Str(title) }) })

  local newContent = { titlePara }
  if #remaining > 0 then
    table.insert(newContent, pandoc.Para(remaining))
  end
  for i = 2, #el.content do
    table.insert(newContent, el.content[i])
  end

  local classes = { "callout" }
  if kind == "important" then table.insert(classes, "important") end

  return pandoc.Div(newContent, pandoc.Attr("", classes))
end
