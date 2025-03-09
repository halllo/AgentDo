$response = Invoke-RestMethod -Uri http://localhost:1234/v1/chat/completions -Method Post -ContentType "application/json" -Body '{
    "model": "hermes-3-llama-3.2-3b",
    "messages": [
        { "role": "system", "content": "Always answer in rhymes." },
        { "role": "user", "content": "What day is it today?" }
    ],
     "tools": [
        {
            "type": "function",
            "function": {
                "name": "get_date",
                "description": "Get current date.",
                "parameters": {
                    "type": "object",
                    "properties": {},
                    "required": []
                }
            }
        }
    ],
    "temperature": 0.7,
    "max_tokens": -1,
    "stream": false
}'

# Output the response body
$response | ConvertTo-Json