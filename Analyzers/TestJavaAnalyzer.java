import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;

public class TestJavaAnalyzer {
    public static void main(String[] args) throws Exception {
        String input = readAllStdin();
        int count = countObjectsInDataArray(input);

        // Analyzer contract: stdout contains one JSON object.
        System.out.println("{\"status\":\"ok\",\"recordsCount\":" + count + ",\"message\":\"Records count: " + count + "\"}");
        System.err.println("TestJavaAnalyzer counted records: " + count);
    }

    private static String readAllStdin() throws Exception {
        BufferedReader reader = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8));
        StringBuilder sb = new StringBuilder();
        String line;
        while ((line = reader.readLine()) != null) {
            sb.append(line).append('\n');
        }
        return sb.toString();
    }

    private static int countObjectsInDataArray(String json) {
        int keyIndex = json.indexOf("\"data\"");
        if (keyIndex < 0) return 0;

        int arrayStart = json.indexOf('[', keyIndex);
        if (arrayStart < 0) return 0;

        boolean inString = false;
        boolean escape = false;
        int bracketDepth = 0;
        int objectDepth = 0;
        int count = 0;

        for (int i = arrayStart; i < json.length(); i++) {
            char c = json.charAt(i);

            if (escape) {
                escape = false;
                continue;
            }

            if (c == '\\' && inString) {
                escape = true;
                continue;
            }

            if (c == '"') {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '[') {
                bracketDepth++;
                continue;
            }

            if (c == ']') {
                bracketDepth--;
                if (bracketDepth == 0) break;
                continue;
            }

            if (bracketDepth == 1 && c == '{') {
                if (objectDepth == 0) count++;
                objectDepth++;
                continue;
            }

            if (bracketDepth == 1 && c == '}') {
                objectDepth--;
            }
        }

        return count;
    }
}
