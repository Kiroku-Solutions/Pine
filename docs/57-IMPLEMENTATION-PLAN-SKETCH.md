# 57 — Implementation Plan & Technical SRS Sketch (Draft)

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-57-IMPLEMENTATION-PLAN-SKETCH`                                             |
| Status           | Non-normative — historical sketch                                                    |
| Reconciled       | 2026-08-31 · Superseded by `56-IMPLEMENTATION-PLAN` and by the audit's own roadmap. |
| Owner            | Engineering Review Board & PM Office                                                 |
| Created          | 2026-08-24                                                                           |
| Last Updated     | 2026-08-25                                                                           |
| Related Documents| `PINE-DOC-56-IMPLEMENTATION-PLAN`, `PINE-DOC-02-PRODUCT-SCOPE`                       |

## 1. Executive Summary & PM Assessment (SRS vs Implementation Plan)

As requested, this document has been completely restructured and translated to English. Regarding whether we need a Software Requirements Specification (SRS) or an Implementation Plan: **we need a hybrid approach for this specific epic**. The overarching product scope is defined in `02-Product-Scope.md`, but the features requested (OCR pipeline, Local AI suggestions, Savings Goals) involve complex technical requirements *and* execution steps. Therefore, this document serves as a **Technical Implementation Plan** that incorporates **System Requirements Specification (SRS)** elements specifically for the AI and Savings epic.

## 2. Engineering Posture & Technical Standards

The implementation will rigorously adhere to the following established standards, ensuring alignment with Pine's core principles:

- **Single Source of Logic:** All business logic, AI orchestration, calculations, and domain rules must reside in the shared core (`Pine.Core` and `Pine.Application`). The platform-specific projects (Windows, Android, iOS, macOS) and UI are strictly **consumers** of this central logic.
- **Shared Multi-platform UI:** `Pine.Ui.Avalonia` will contain all presentation logic (ViewModels) using MVVM Toolkit 8.3. The UI must be highly polished, featuring smooth animations and "Pine Orange" glassmorphism to meet premium design standards.
- **Architecture:** Hexagonal / Clean Architecture (`Pine.Core`, `Pine.Application`, `Pine.Infrastructure`).
- **Patterns:** Domain-Driven Design (Entities, Value Objects) and CQRS (MediatR).
- **Local-First AI:** All AI models (OCR, classification, recommendations) must run **on-device** with zero external dependencies. Privacy by default is non-negotiable.
- **High-Level Data Management:** Secure persistence using SQLCipher via Entity Framework Core 8, supporting Complex Types and ensuring data integrity for advanced financial analysis. [Current Status: Implemented in PineDbContext]

## 3. Implementation Phases

The project is divided into strictly defined phases to ensure incremental delivery of the OCR pipeline, local AI training, and savings goal recommendations.

### Phase 1: OCR Pipeline & Data Extraction
*Focus: Capturing and digitizing receipt data accurately.*

1. **Image Pre-processing & Cropping (UI/UX):** [Current Status: Implemented]
   - **UI Component:** Implement an intuitive image cropping tool in the Avalonia UI allowing users to adjust receipt boundaries before processing. This ensures the OCR engine only reads relevant data.
   - **Infrastructure:** Integrate a local lightweight image processing library (e.g., ImageSharp) to handle rotation, contrast enhancement, and noise reduction to prepare the image for OCR.
2. **On-Device OCR Engine:** [Current Status: Implemented]
   - Integrate a local OCR engine (e.g., Tesseract.NET or a quantized local ONNX vision model) to extract raw text from the cropped image.
   - *Actual Implementation:* We are currently using `Sdcb.PaddleOCR` (PP-OCRv5 Latin models) via `PaddleOcrEngine.cs`, which successfully extracts text and bounding boxes entirely on-device using OpenCV.

### Phase 2: AI-Driven Auto-Cataloging (NLP & Classification)
*Focus: Transforming raw OCR text into structured financial data.*

1. **Information Parsing & Cataloging:** [Current Status: Partially Implemented (Heuristics)]
   - Utilize a small, locally-hosted NLP model (e.g., a quantized BERT or small LLM via ONNX Runtime) to perform Named Entity Recognition (NER).
   - **Data Points to Extract:** Merchant/Store Name, Date, Total Amount, Individual Items, and Taxes.
   - *Actual Implementation:* We currently have `HeuristicFieldExtractor.cs` for basic extraction, but true NLP/NER is pending.
   - **Auto-Categorization:** Automatically map merchants to categories (e.g., "Walmart" -> "Groceries", "Uber" -> "Transport") using local heuristics and historical user data.
2. **Continuous Local Improvement (On-Device Learning):** [Current Status: Implemented]
   - **Mechanism:** To improve the model locally on the user's device without exposing data, we will implement **Active Learning** and **Local Weight Updating**.
   - *Actual Implementation:* `MlNetCategorizer.cs` already implements on-device model training (`TrainModelAsync`) using ML.NET's `SdcaMaximumEntropy` trainer, saving updated models directly to disk.

### Phase 3: Smart Financial Suggestions & Analytics
*Focus: Providing actionable insights based on categorized data.* [Current Status: Pending]

1. **Small Specialized Financial AI:**
   - Deploy a highly specialized, small-footprint model focused solely on financial pattern recognition.
   - **Pattern Detection:** The model will analyze aggregated data to find trends. For example, comparing unit prices or overall spend across different merchants (e.g., "You spend 20% more on groceries at Supermarket A compared to Supermarket B").
2. **Actionable Insights Engine:**
   - Generate proactive, localized suggestions displayed elegantly on the Dashboard. Example: "Consider shopping at Supermarket B for your weekly groceries to save approximately $15/week based on your recent purchases."

### Phase 4: Savings Goals & Gamified Recommendations
*Focus: Helping users achieve financial targets through behavioral changes.* [Current Status: Pending]

1. **Savings Goals Domain Implementation:**
   - Create the `Goal` entity in `Pine.Core` (`TargetName`, `TargetAmount`, `CurrentAmount`, `TargetDate`).
   - Implement CQRS handlers for contributing to and modifying goals.
2. **AI-Tailored Savings Paths:**
   - The Local AI analyzes discretionary spending (e.g., ultra-processed cereals, subscriptions) and calculates how cutting back can fund a specific goal.
   - **Example Scenario:** User sets a goal for an $800 iPhone. The AI detects $40/month spent on ultra-processed cereals. It suggests: *"If you reduce your cereal spending to $10/month, you can reach your iPhone goal 2 months faster."*
3. **Polished UI Integration:**
   - Create a reactive ViewModel (`GoalsViewModel`) and a fluid interface (`GoalsView.axaml`).
   - Use high-quality visual indicators (progress bars, glassmorphism cards, micro-animations) to make reaching goals feel rewarding. The UI must feel responsive, alive, and premium.

## 4. Open Questions & Next Steps

Before proceeding with Phase 1, the PM Office requests clarification on the following:

1. **OCR Technology Stack:** Shall we proceed with Tesseract (traditional, reliable, but requires good image quality) or a quantized Vision-Language Model (VLM) running via ONNX for the OCR step?
2. **Local Model Updating:** Implementing on-device fine-tuning (LoRA/ML.NET) requires significant device resources. Should we limit model updating to desktop/laptop environments, or attempt it on mobile devices while charging?
3. **Goal Funding Source:** Are Savings Goals purely virtual (funded by general account balances), or do they require transferring money to a dedicated "Vault/Stash" account?
